using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using SpacetimeDB.Types;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Generic SpacetimeDB-backed implementation of IBasicRepository&lt;TModel&gt;, replacing
    /// BasicRepository&lt;TModel&gt; the same way SpacetimeTagRepository did by hand in Phase 1 -
    /// this is that pattern generalized so porting each entity is a small subclass, not a fresh
    /// implementation of the whole interface every time.
    ///
    /// TStdbRow is the entity's generated SpacetimeDB row type (e.g. SpacetimeDB.Types.RootFolder)
    /// - concrete subclasses supply the handful of entity-specific pieces (Table, FindRowById,
    /// ToModel/GetRowId, the Invoke*Reducer delegates, and SubscribeOwnUpdateCommitted) that can't
    /// be generalized because each generated table/reducer is its own type.
    ///
    /// Reads: every read (All/Get/Find/Get(ids)/Single/SingleOrDefault) runs against the locally
    /// subscribed client cache (Table.Iter()/Table.Id.Find(id)) via Conn.RunOnActor, not an ad-hoc
    /// RemoteQuery network round-trip - SpacetimeDbConnection already keeps this cache live via
    /// SubscribeToAllTables(), so querying it locally instead of asking the server again is both
    /// faster and the actually-idiomatic way to read from SpacetimeDB.
    ///
    /// Writes: every write (Insert/Update/SetFields/Delete) is confirmed by matching a table row
    /// event's CallerIdentity/CallerConnectionId against this connection's own (see
    /// IsOwnConnectionEvent) - not by re-querying and guessing which row is "probably" ours. Update
    /// additionally accepts a correlated reducer-committed result (via SubscribeOwnUpdateCommitted)
    /// as alternate confirmation, because a no-op update (the new row content is identical to the
    /// old) commits successfully but produces no row-delta event at all - waiting on the row event
    /// alone would time out on a legitimate, successful write. All callback registration/
    /// unregistration and every Table/Db touch happens via Conn.RunOnActor, which serializes it
    /// against the connection's FrameTick() pump on one dedicated thread - the SDK's own generated
    /// event-listener storage is an unsynchronized List/Dictionary, so registering or invoking a
    /// callback from two different threads at once is a real race, not just a style concern.
    /// Writes are additionally serialized per repository instance with a lock (see
    /// PinRealRepositoriesByDefault-adjacent remarks in CompositionExtensions.cs for why every
    /// entity must resolve to exactly one singleton instance for this to mean anything), so only
    /// one entity's table is blocked at a time; a different entity's repository (sharing the same
    /// underlying connection) writes independently.
    /// </summary>
    public abstract class SpacetimeBasicRepository<TModel, TStdbRow> : IBasicRepository<TModel>
        where TModel : ModelBase, new()
        where TStdbRow : class, SpacetimeDB.BSATN.IStructuralReadWrite, new()
    {
        protected readonly ISpacetimeDbConnection Conn;
        private readonly IEventAggregator _eventAggregator;
        private readonly object _writeLock = new object();

        protected SpacetimeBasicRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
        {
            Conn = connection;
            _eventAggregator = eventAggregator;
        }

        /// <summary>
        /// The generated table handle for this entity (e.g. Conn.Connection.Db.Tag) - every
        /// SpacetimeDB table handle shares this same generic base regardless of row type, which
        /// is what lets Insert/Update/SetFields/Delete below subscribe to OnInsert/OnUpdate/
        /// OnDelete generically instead of needing a bespoke hook per entity. Only ever touched
        /// via Conn.RunOnActor - never read or subscribed to directly.
        /// </summary>
        protected abstract RemoteTableHandle<EventContext, TStdbRow> Table { get; }

        /// <summary>
        /// Primary-key lookup against the locally subscribed cache (e.g. Table.Id.Find(id)) -
        /// every entity has a [PrimaryKey] column and therefore a generated unique-index accessor
        /// for it. Only ever called from inside a Conn.RunOnActor delegate.
        /// </summary>
        protected abstract TStdbRow FindRowById(int id);

        protected abstract TModel ToModel(TStdbRow row);

        protected abstract int GetRowId(TStdbRow row);

        protected abstract void InvokeInsertReducer(TModel model);

        protected abstract void InvokeUpdateReducer(TModel model);

        protected abstract void InvokeDeleteReducer(int id);

        /// <summary>
        /// Wires this entity's generated per-reducer result event (e.g.
        /// Conn.Connection.Reducers.OnUpdateTag) to onCommitted, invoked only when the result is
        /// both caused by this connection and Status.Committed - the second confirmation channel
        /// for Update (see class remarks on why a row event alone isn't enough). Returns an
        /// IDisposable that unsubscribes; called and disposed only from inside Conn.RunOnActor.
        /// </summary>
        protected abstract IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted);

        protected virtual bool PublishModelEvents => false;

        public virtual IEnumerable<TModel> All() =>
            Conn.RunOnActor(() => Table.Iter().Select(ToModel).ToList());

        public int Count() => Conn.RunOnActor(() => Table.Count);

        public bool HasItems() => Conn.RunOnActor(() => Table.Count > 0);

        public TModel Find(int id) => Conn.RunOnActor(() =>
        {
            var row = FindRowById(id);
            return row == null ? null : ToModel(row);
        });

        public TModel Get(int id)
        {
            var model = Find(id);

            if (model == null)
            {
                throw new ModelNotFoundException(typeof(TModel), id);
            }

            return model;
        }

        public IEnumerable<TModel> Get(IEnumerable<int> ids)
        {
            var idSet = ids.ToHashSet();

            if (idSet.Count == 0)
            {
                return Array.Empty<TModel>();
            }

            var result = Conn.RunOnActor(() => Table.Iter().Where(r => idSet.Contains(GetRowId(r))).Select(ToModel).ToList());

            if (result.Count != idSet.Count)
            {
                throw new ApplicationException($"Expected query to return {idSet.Count} rows but returned {result.Count}");
            }

            return result;
        }

        public TModel Single() => Conn.RunOnActor(() => Table.Iter().Select(ToModel).Single());

        public TModel SingleOrDefault() => Conn.RunOnActor(() => Table.Iter().Select(ToModel).SingleOrDefault());

        /// <summary>
        /// Runs a read against the locally subscribed cache, materializing the result before it
        /// leaves the actor thread - for a leaf repository's own bespoke predicate finders (e.g.
        /// SpacetimeTagRepository.FindByLabel), which used to build a RemoteQuery WHERE fragment
        /// and now query Table.Iter() (or a generated index handle) directly instead. Never return
        /// a raw IEnumerable/Table.Iter() handle from the query delegate - it must not escape the
        /// actor thread, since enumerating it concurrently with FrameTick() is exactly the race
        /// this class exists to avoid.
        /// </summary>
        protected TResult Query<TResult>(Func<RemoteTableHandle<EventContext, TStdbRow>, TResult> query) =>
            Conn.RunOnActor(() => query(Table));

        // Opt-in hook for the migration tool (Sonarr.SpacetimeMigration) cutting an existing
        // SQLite-backed install over to SpacetimeDB - unlike Insert(), which always assigns a
        // fresh id, this preserves model.Id exactly so foreign keys (Episode.SeriesId, etc.)
        // transfer as-is with no in-script id-remapping needed. Only overridden by the handful of
        // repositories the migration's first pass covers - everything else throws until the
        // migration's scope grows to match.
        public virtual void MigrateInsert(TModel model) =>
            throw new NotSupportedException($"{GetType().Name} does not support migration inserts");

        /// <summary>
        /// Runs the given MigrateInsert&lt;Entity&gt; reducer invocation inside this repository
        /// instance's write lock AND waits for a correlated OnInsert row event for the given
        /// (preserved) id before returning - a leaf's MigrateInsert override should call the
        /// entity's MigrateInsert&lt;Entity&gt; reducer through this rather than invoking it
        /// directly. Merely sending the reducer under the lock and returning immediately (an
        /// earlier version of this helper did exactly that) is not enough: releasing the lock
        /// before the row actually lands leaves a window where a concurrent Insert() through this
        /// same instance can start waiting for its own OnInsert event before the migrated row's
        /// event has arrived, and since Insert's handler only checks connection identity (it
        /// can't know its own future id yet), it would wrongly adopt the migrated row as its own.
        /// Waiting here for the specific id (known in advance, unlike a normal Insert) closes that
        /// gap, and holding the lock for the same duration Insert/Update/Delete already do means
        /// only one write of any kind is ever in flight per repository instance.
        /// </summary>
        protected void InvokeAndWaitForMigrateInsert(int id, Action invokeReducer)
        {
            lock (_writeLock)
            {
                Exception failure = null;
                var confirmed = new ManualResetEventSlim(false);

                void Handler(EventContext ctx, TStdbRow row)
                {
                    if (!IsOwnConnectionEvent(ctx) || GetRowId(row) != id)
                    {
                        return;
                    }

                    confirmed.Set();
                }

                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmed.Set();
                });

                Conn.RunOnActor(() => Table.OnInsert += Handler);

                try
                {
                    invokeReducer();

                    if (!confirmed.Wait(WriteConfirmationTimeout))
                    {
                        throw new InvalidOperationException(
                            $"{typeof(TModel).Name}: migrate-insert of id {id} was not confirmed within {WriteConfirmationTimeout} - the reducer call " +
                            "may have failed, or the connection to SpacetimeDB is unresponsive.");
                    }

                    if (failure != null)
                    {
                        throw new InvalidOperationException($"{typeof(TModel).Name}: migrate-insert of id {id}'s outcome is unknown - the connection was lost while waiting for confirmation.", failure);
                    }
                }
                finally
                {
                    Conn.RunOnActor(() => Table.OnInsert -= Handler);
                }
            }
        }

        /// <summary>
        /// For a bulk-mutating reducer that doesn't correlate to any single row id (e.g.
        /// SpacetimeCommandRepository.OrphanStarted, which updates every matching row in one
        /// call) - invokes it inside this repository instance's write lock and waits for a
        /// correlated reducer-committed result (identity/connectionId match) before returning.
        /// Holding the lock through confirmation, the same way every other write path does, means
        /// no per-row Insert/Update/Delete/MigrateInsert wait on this repository instance can be
        /// concurrently active while this bulk reducer's own confirmation is pending - closing
        /// the collision a bulk reducer's row events would otherwise create for whichever per-row
        /// wait happened to be watching the same table at the same time.
        /// </summary>
        protected void InvokeAndWaitForReducerCommitted(Action invokeReducer, Func<Action, IDisposable> subscribeCommitted)
        {
            lock (_writeLock)
            {
                Exception failure = null;
                var confirmed = new ManualResetEventSlim(false);

                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmed.Set();
                });

                var subscription = Conn.RunOnActor(() => subscribeCommitted(() => confirmed.Set()));

                try
                {
                    invokeReducer();

                    if (!confirmed.Wait(WriteConfirmationTimeout))
                    {
                        throw new InvalidOperationException($"{GetType().Name}: reducer call was not confirmed within {WriteConfirmationTimeout} - it may have failed, or the connection to SpacetimeDB is unresponsive.");
                    }

                    if (failure != null)
                    {
                        throw new InvalidOperationException($"{GetType().Name}: reducer call's outcome is unknown - the connection was lost while waiting for confirmation.", failure);
                    }
                }
                finally
                {
                    Conn.RunOnActor(() => subscription.Dispose());
                }
            }
        }

        public TModel Insert(TModel model)
        {
            if (model.Id != 0)
            {
                throw new InvalidOperationException("Can't insert model with existing ID " + model.Id);
            }

            lock (_writeLock)
            {
                TStdbRow insertedRow = null;
                Exception failure = null;
                var confirmed = new ManualResetEventSlim(false);

                void Handler(EventContext ctx, TStdbRow row)
                {
                    if (!IsOwnConnectionEvent(ctx))
                    {
                        return;
                    }

                    insertedRow = row;
                    confirmed.Set();
                }

                // Registered before the row-event handler, not after: if the connection is lost in
                // the gap between the two, RegisterPendingOperation must already be listening so
                // HandleDisconnect's sweep of currently-pending operations doesn't miss this one
                // (a disconnect that lands in that gap would otherwise go unnoticed until the full
                // WriteConfirmationTimeout elapses instead of failing immediately). Setting
                // `confirmed` before Wait() is even called is harmless - ManualResetEventSlim is a
                // persistent signal, not a one-shot handoff, so a Set() that "arrives early" is
                // still observed correctly.
                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmed.Set();
                });

                // Registered (and later removed) exclusively via RunOnActor, so it can never race
                // FrameTick()'s own invocation of this same event on the actor thread. Registered
                // before invoking the reducer, inside the same lock a different Insert() call
                // through this instance would also need - only one insert to this table via this
                // repository can be in flight at a time, so the first OnInsert event we see that's
                // confirmed as OUR OWN connection's doing (not a genuinely different connection's
                // insert into the same table, which fires this same event but with a different
                // CallerIdentity/CallerConnectionId, and is simply ignored) has to be this call's
                // row. No guessing which of several new ids is ours the way a polling-based "grab
                // the current max id" approach would have to.
                Conn.RunOnActor(() => Table.OnInsert += Handler);

                try
                {
                    InvokeInsertReducer(model);

                    if (!confirmed.Wait(WriteConfirmationTimeout))
                    {
                        throw new InvalidOperationException(
                            $"{typeof(TModel).Name}: insert was not confirmed within {WriteConfirmationTimeout} - the reducer call may " +
                            "have failed, or the connection to SpacetimeDB is unresponsive. Not assigning an id.");
                    }

                    if (failure != null)
                    {
                        throw new InvalidOperationException($"{typeof(TModel).Name}: insert's outcome is unknown - the connection was lost while waiting for confirmation.", failure);
                    }
                }
                finally
                {
                    Conn.RunOnActor(() => Table.OnInsert -= Handler);
                }

                model.Id = GetRowId(insertedRow);

                // Runs while still holding _writeLock, with model.Id now correctly resolved -
                // the hook a subclass needing an additional reducer call keyed by the new row's
                // id (e.g. SpacetimeSeriesRepository replacing the new Series' tags) should use.
                AfterInsertIdResolved(model);
            }

            PublishModelEvent(model, ModelAction.Created);

            return model;
        }

        /// <summary>
        /// Extension point for a subclass that needs one more reducer call keyed by the newly
        /// assigned id as part of the same insert (e.g. a junction-table replace) - runs inside
        /// Insert's lock, after model.Id has been safely resolved. No-op by default.
        /// </summary>
        protected virtual void AfterInsertIdResolved(TModel model)
        {
        }

        // Bounded safety-net timeout for the ManualResetEventSlim waits below - the ordinary path
        // resolves as soon as the corresponding row/reducer event arrives, usually well under
        // this; this only matters if the reducer call fails silently or the connection is
        // unresponsive. Overridable purely so an integration test against a live server can
        // shrink it instead of waiting out the full default to exercise the timeout path -
        // production code never overrides this.
        protected virtual TimeSpan WriteConfirmationTimeout => TimeSpan.FromSeconds(5);

        // True if the given table-row event was caused by a reducer call from THIS connection,
        // not a genuinely different connection also writing to the same table. EventContext.Event
        // is a tagged union (SpacetimeDB.Event<Reducer>) - SubscribeApplied/UnsubscribeApplied
        // cases never apply here (this is only ever checked from row-changed callbacks, which
        // only fire for the Reducer case), but the pattern match still has to name the case
        // explicitly rather than assume it.
        private bool IsOwnConnectionEvent(EventContext ctx) =>
            ctx.Event is Event<Reducer>.Reducer reducerCase &&
            reducerCase.ReducerEvent.CallerIdentity == Conn.Connection.Identity &&
            reducerCase.ReducerEvent.CallerConnectionId == Conn.Connection.ConnectionId;

        public void InsertMany(IList<TModel> models)
        {
            if (models.Any(x => x.Id != 0))
            {
                throw new InvalidOperationException("Can't insert model with existing ID != 0");
            }

            foreach (var model in models)
            {
                Insert(model);
            }
        }

        public TModel Update(TModel model)
        {
            if (model.Id == 0)
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            lock (_writeLock)
            {
                InvokeAndWaitForUpdate(model.Id, () => InvokeUpdateReducer(model));
            }

            PublishModelEvent(model, ModelAction.Updated);

            return model;
        }

        public void UpdateMany(IList<TModel> models)
        {
            if (models.Any(x => x.Id == 0))
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            foreach (var model in models)
            {
                Update(model);
            }
        }

        public TModel Upsert(TModel model) => model.Id == 0 ? Insert(model) : Update(model);

        public void SetFields(TModel model, params Expression<Func<TModel, object>>[] properties)
        {
            if (model.Id == 0)
            {
                throw new InvalidOperationException("Attempted to update model without ID");
            }

            // InvokeUpdateReducer always replaces the whole row (SpacetimeDB has no equivalent
            // of a SQL partial-column UPDATE via a simple parameterized reducer) - unlike
            // SQL's SetFields, which only touches the named columns. model here is typically
            // only partially populated by the caller (see e.g. ScheduledTask.SetLastExecutionTime),
            // so merge the named properties onto the current full row before replacing it,
            // rather than clobbering every other column with its default value.
            //
            // The read-merge-write-and-wait-for-confirmation has to happen as one unit inside
            // _writeLock: two SetFields calls for the same id through this same instance, setting
            // different fields, would otherwise both read the same pre-update row before either
            // writes, each merge only their own field onto that stale copy, and whichever write
            // lands last silently discards the other's change - a lost update, not merely stale
            // data, despite both calls going through the one locked instance. Waiting for this
            // call's own update to be confirmed (via InvokeAndWaitForUpdate) before releasing the
            // lock closes the other half of the same problem: without it, the next SetFields
            // call in line for the lock could still read a not-yet-applied row.
            TModel current;

            lock (_writeLock)
            {
                current = Get(model.Id);

                foreach (var property in properties.Select(p => p.GetMemberName()))
                {
                    property.SetValue(current, property.GetValue(model));
                }

                InvokeAndWaitForUpdate(current.Id, () => InvokeUpdateReducer(current));
            }

            PublishModelEvent(current, ModelAction.Updated);
        }

        // Shared by Update() and SetFields(): invokes the given reducer call and blocks until
        // EITHER an OnUpdate row event, or this entity's own reducer-committed result, confirms
        // it landed for the specific row id being updated. Both channels are needed: a genuine
        // no-op update (the new row content is byte-identical to what was already there) commits
        // successfully server-side but produces no row-delta event at all, since there is nothing
        // to broadcast - waiting on the row event alone would time out on a legitimate write. Both
        // handlers are filtered to events this connection itself caused (see IsOwnConnectionEvent)
        // for the specific row id, not just "any update to this table", in case a different
        // connection updates a different row while this one is waiting.
        private void InvokeAndWaitForUpdate(int id, Action invokeReducer)
        {
            Exception failure = null;
            var confirmed = new ManualResetEventSlim(false);

            void RowHandler(EventContext ctx, TStdbRow oldRow, TStdbRow newRow)
            {
                if (!IsOwnConnectionEvent(ctx) || GetRowId(newRow) != id)
                {
                    return;
                }

                confirmed.Set();
            }

            void CommittedHandler(int committedId)
            {
                if (committedId != id)
                {
                    return;
                }

                confirmed.Set();
            }

            using var pendingOp = Conn.RegisterPendingOperation(ex =>
            {
                failure = ex;
                confirmed.Set();
            });

            Conn.RunOnActor(() => Table.OnUpdate += RowHandler);
            var committedSubscription = Conn.RunOnActor(() => SubscribeOwnUpdateCommitted(CommittedHandler));

            try
            {
                invokeReducer();

                if (!confirmed.Wait(WriteConfirmationTimeout))
                {
                    throw new InvalidOperationException(
                        $"{typeof(TModel).Name}: update to id {id} was not confirmed within {WriteConfirmationTimeout} - the reducer call " +
                        "may have failed, or the connection to SpacetimeDB is unresponsive.");
                }

                if (failure != null)
                {
                    throw new InvalidOperationException($"{typeof(TModel).Name}: update to id {id}'s outcome is unknown - the connection was lost while waiting for confirmation.", failure);
                }
            }
            finally
            {
                Conn.RunOnActor(() =>
                {
                    Table.OnUpdate -= RowHandler;
                    committedSubscription.Dispose();
                });
            }
        }

        // One outer lock around the whole batch, not just each per-model SetFields' own inner
        // lock - _writeLock is a plain Monitor, safe to re-enter from the same thread, so this
        // costs nothing extra but closes a real gap: without it, another caller through this same
        // instance could interleave a write between two models in what the caller of this method
        // intended as one batch, the same way a bare loop of single-row SetFields calls could
        // for any other repository.
        public void SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties)
        {
            lock (_writeLock)
            {
                foreach (var model in models)
                {
                    SetFields(model, properties);
                }
            }
        }

        public void Delete(TModel model) => Delete(model.Id);

        public void Delete(int id)
        {
            lock (_writeLock)
            {
                Exception failure = null;
                var confirmed = new ManualResetEventSlim(false);

                void Handler(EventContext ctx, TStdbRow row)
                {
                    if (!IsOwnConnectionEvent(ctx) || GetRowId(row) != id)
                    {
                        return;
                    }

                    confirmed.Set();
                }

                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmed.Set();
                });

                Conn.RunOnActor(() => Table.OnDelete += Handler);

                try
                {
                    InvokeDeleteReducer(id);

                    if (!confirmed.Wait(WriteConfirmationTimeout))
                    {
                        throw new InvalidOperationException(
                            $"{typeof(TModel).Name}: delete of id {id} was not confirmed within {WriteConfirmationTimeout} - the reducer call " +
                            "may have failed, or the connection to SpacetimeDB is unresponsive.");
                    }

                    if (failure != null)
                    {
                        throw new InvalidOperationException($"{typeof(TModel).Name}: delete of id {id}'s outcome is unknown - the connection was lost while waiting for confirmation.", failure);
                    }
                }
                finally
                {
                    Conn.RunOnActor(() => Table.OnDelete -= Handler);
                }
            }
        }

        public void DeleteMany(List<TModel> models)
        {
            DeleteMany(models.Select(m => m.Id));
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            foreach (var id in ids)
            {
                Delete(id);
            }
        }

        public void Purge(bool vacuum = false)
        {
            foreach (var model in All().ToList())
            {
                Delete(model.Id);
            }
        }

        public virtual PagingSpec<TModel> GetPaged(PagingSpec<TModel> pagingSpec)
        {
            var query = All().AsEnumerable();

            foreach (var filter in pagingSpec.FilterExpressions)
            {
                query = query.Where(filter.Compile());
            }

            var allMatching = query.ToList();
            pagingSpec.TotalRecords = allMatching.Count;

            var sorted = pagingSpec.SortDirection == SortDirection.Descending
                ? allMatching.OrderByDescending(GetSortKey)
                : allMatching.OrderBy(GetSortKey);

            pagingSpec.Records = sorted
                .Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize)
                .Take(pagingSpec.PageSize)
                .ToList();

            return pagingSpec;
        }

        /// <summary>
        /// Sort key for GetPaged's default in-memory sort. Base implementation sorts by Id;
        /// override for entities where the real repository sorts by a different column.
        /// </summary>
        protected virtual object GetSortKey(TModel model) => model.Id;

        // Mirrors BasicRepository.ModelUpdated - lets a subclass force an update event to publish
        // for a specific call even when PublishModelEvents is false for the entity as a whole
        // (e.g. the real EpisodeRepository does this for SetMonitoredFlat/SetFileId/ClearFileId).
        protected void ModelUpdated(TModel model, bool forcePublish = false) => PublishModelEvent(model, ModelAction.Updated, forcePublish);

        private void PublishModelEvent(TModel model, ModelAction action, bool forcePublish = false)
        {
            if (PublishModelEvents || forcePublish)
            {
                _eventAggregator.PublishEvent(new ModelEvent<TModel>(model, action));
            }
        }

        /// <summary>
        /// Small reusable IDisposable for a leaf's SubscribeOwnUpdateCommitted override to hand
        /// back - disposing it unsubscribes the generated per-reducer event it wired.
        /// </summary>
        protected sealed class Unsubscriber : IDisposable
        {
            private readonly Action _unsubscribe;

            public Unsubscriber(Action unsubscribe) => _unsubscribe = unsubscribe;

            public void Dispose() => _unsubscribe();
        }
    }
}
