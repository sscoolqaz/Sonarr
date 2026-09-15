using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
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
    /// Phase 3: every public method is genuinely async (Task-returning throughout, not a sync
    /// facade blocking on an async core) - callers await Conn.RunOnActorAsync rather than
    /// Conn.RunOnActor, so a caller's own thread (an ASP.NET request thread, ultimately) is freed
    /// while the actor thread processes the queued work on its own schedule, instead of parking a
    /// thread for the duration. This also means _writeLock can no longer be a plain Monitor lock
    /// (you cannot hold a lock across an await - the continuation may resume on a different
    /// thread than took the lock, and Monitor.Exit from a non-owning thread throws) - it's a
    /// SemaphoreSlim(1,1) instead, acquired with WaitAsync and released in a finally.
    ///
    /// Reads: every read (All/Get/Find/Get(ids)/Single/SingleOrDefault) runs against the locally
    /// subscribed client cache (Table.Iter()/Table.Id.Find(id)) via Conn.RunOnActorAsync, not an
    /// ad-hoc RemoteQuery network round-trip - SpacetimeDbConnection already keeps this cache live
    /// via SubscribeToAllTables(), so querying it locally instead of asking the server again is
    /// both faster and the actually-idiomatic way to read from SpacetimeDB.
    ///
    /// Writes: every write (Insert/Update/SetFields/Delete) is confirmed by matching a
    /// correlated event's CallerIdentity/CallerConnectionId against this connection's own (see
    /// IsOwnConnectionEvent) - not by re-querying and guessing which row is "probably" ours.
    /// Update and Delete are confirmed EXCLUSIVELY through the entity's reducer-committed result
    /// (SubscribeOwnUpdateCommitted / SubscribeOwnDeleteCommitted) - that single channel already
    /// reports Status.Committed for a genuine no-op update that would otherwise produce no
    /// row-delta event at all, and reports failure (Status.Failed/OutOfEnergy) immediately for a
    /// rejected reducer call, so a row-event watch alongside it would be redundant, not additive
    /// FOR CONFIRMATION PURPOSES; neither Update's nor Delete's own confirmation wait registers
    /// its own Table.OnUpdate/OnDelete handler the way Insert's confirmation wait registers
    /// Table.OnInsert. Insert is the one write that still needs two channels for its OWN
    /// confirmation: the server assigns the row's id itself (auto-increment), and only the
    /// Table.OnInsert row event ever carries it back, so that event remains the sole,
    /// authoritative source of a successful insert's id. Insert's own reducer-committed channel
    /// (SubscribeOwnInsertCommitted) is wired for failure-fast only (Status.Failed/OutOfEnergy) -
    /// its onCommitted callback is deliberately a no-op, so a successful reducer-committed result
    /// can never race ahead of the row event and resolve the wait before insertedRow has actually
    /// been populated. All callback registration/unregistration and every Table/Db touch happens
    /// via Conn.RunOnActorAsync, which serializes it against the connection's FrameTick() pump on
    /// one dedicated thread - the SDK's own generated event-listener storage is an unsynchronized
    /// List/Dictionary, so registering or invoking a callback from two different threads at once
    /// is a real race, not just a style concern. Writes are additionally serialized per
    /// repository instance with _writeLock, so only one entity's table is blocked at a time; a
    /// different entity's repository (sharing the same underlying connection) writes
    /// independently.
    ///
    /// Event ownership beyond this connection's own writes: separately from the per-write
    /// confirmation channels above, EnsureRowEventListenersRegisteredAsync registers one
    /// ALWAYS-ON Table.OnInsert/OnUpdate/OnDelete handler per repository instance (once, lazily,
    /// on first use - see that method's remarks) that watches for row changes THIS connection did
    /// NOT cause (a scheduled/bulk reducer, another writer sharing the database, a migration
    /// tool) and publishes a ModelEvent&lt;TModel&gt; for them via PublishModelEvent, gated by
    /// IsOwnConnectionEvent so a self-caused write - already published by the explicit
    /// PublishModelEvent call after Insert/Update/SetFields/Delete confirms - is never published
    /// twice. This is what makes Sonarr's own ModelEvent&lt;TModel&gt; (driving UI SignalR pushes,
    /// cache invalidation, etc.) fire for externally-caused row changes too, not just this
    /// connection's own confirmed writes. See that method and OnForeignRowInserted/
    /// OnForeignRowUpdated/OnForeignRowDeleted for the full remarks, including the accepted
    /// register-once-never-unregister simplification for this codebase's singleton-lifetime
    /// repositories.
    /// </summary>
    public abstract class SpacetimeBasicRepository<TModel, TStdbRow> : IBasicRepository<TModel>
        where TModel : ModelBase, new()
        where TStdbRow : class, SpacetimeDB.BSATN.IStructuralReadWrite, new()
    {
        protected readonly ISpacetimeDbConnection Conn;
        private readonly IEventAggregator _eventAggregator;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        // Guards EnsureRowEventListenersRegisteredAsync's one-time registration (see that
        // method's remarks) - a separate lock from _writeLock, since registering the
        // always-on row-event listeners has nothing to do with write serialization and
        // shouldn't contend with it.
        private readonly SemaphoreSlim _rowEventListenerRegistrationLock = new SemaphoreSlim(1, 1);
        private volatile bool _rowEventListenersRegistered;

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
        /// via Conn.RunOnActorAsync - never read or subscribed to directly.
        /// </summary>
        protected abstract RemoteTableHandle<EventContext, TStdbRow> Table { get; }

        /// <summary>
        /// Primary-key lookup against the locally subscribed cache (e.g. Table.Id.Find(id)) -
        /// every entity has a [PrimaryKey] column and therefore a generated unique-index accessor
        /// for it. Only ever called from inside a Conn.RunOnActorAsync delegate.
        /// </summary>
        protected abstract TStdbRow FindRowById(int id);

        protected abstract TModel ToModel(TStdbRow row);

        protected abstract int GetRowId(TStdbRow row);

        protected abstract void InvokeInsertReducer(TModel model);

        protected abstract void InvokeUpdateReducer(TModel model);

        protected abstract void InvokeDeleteReducer(int id);

        /// <summary>
        /// Wires this entity's generated per-reducer result event (e.g.
        /// Conn.Connection.Reducers.OnUpdateTag) to onCommitted (Status.Committed) or onFailed
        /// (Status.Failed / OutOfEnergy) - the sole confirmation channel for Update (see class
        /// remarks: this alone already handles the no-op-update case a row event would miss).
        /// Returns an IDisposable that unsubscribes; called and disposed only from inside
        /// Conn.RunOnActorAsync.
        /// </summary>
        protected abstract IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed);

        /// <summary>
        /// Wires this entity's generated per-reducer result event (e.g.
        /// Conn.Connection.Reducers.OnDeleteTag) to onCommitted (Status.Committed, given the
        /// deleted id) or onFailed (Status.Failed / OutOfEnergy) - the sole confirmation channel
        /// for Delete (see class remarks: Delete needs no row event, only success/failure).
        /// Returns an IDisposable that unsubscribes; called and disposed only from inside
        /// Conn.RunOnActorAsync.
        /// </summary>
        protected abstract IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed);

        /// <summary>
        /// Wires this entity's generated per-reducer result event (e.g.
        /// Conn.Connection.Reducers.OnInsertTag) to onCommitted (Status.Committed) or onFailed
        /// (Status.Failed / OutOfEnergy) - a FAILURE-FAST-ONLY channel for Insert (see class
        /// remarks: unlike Update/Delete, Insert cannot use this channel to signal success, since
        /// it never carries the server-assigned id - only the Table.OnInsert row event does).
        /// Callers must never treat onCommitted as authoritative for a successful insert. Returns
        /// an IDisposable that unsubscribes; called and disposed only from inside
        /// Conn.RunOnActorAsync.
        /// </summary>
        protected abstract IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed);

        protected virtual bool PublishModelEvents => false;

        public virtual async Task<IEnumerable<TModel>> All()
        {
            await EnsureRowEventListenersRegisteredAsync();
            return await Conn.RunOnActorAsync(() => Table.Iter().Select(ToModel).ToList());
        }

        public async Task<int> Count()
        {
            await EnsureRowEventListenersRegisteredAsync();
            return await Conn.RunOnActorAsync(() => Table.Count);
        }

        public async Task<bool> HasItems()
        {
            await EnsureRowEventListenersRegisteredAsync();
            return await Conn.RunOnActorAsync(() => Table.Count > 0);
        }

        public async Task<TModel> Find(int id)
        {
            await EnsureRowEventListenersRegisteredAsync();

            return await Conn.RunOnActorAsync(() =>
            {
                var row = FindRowById(id);
                return row == null ? null : ToModel(row);
            });
        }

        public async Task<TModel> Get(int id)
        {
            var model = await Find(id);

            if (model == null)
            {
                throw new ModelNotFoundException(typeof(TModel), id);
            }

            return model;
        }

        public async Task<IEnumerable<TModel>> Get(IEnumerable<int> ids)
        {
            var idSet = ids.ToHashSet();

            if (idSet.Count == 0)
            {
                return Array.Empty<TModel>();
            }

            await EnsureRowEventListenersRegisteredAsync();

            var result = await Conn.RunOnActorAsync(() => Table.Iter().Where(r => idSet.Contains(GetRowId(r))).Select(ToModel).ToList());

            if (result.Count != idSet.Count)
            {
                throw new ApplicationException($"Expected query to return {idSet.Count} rows but returned {result.Count}");
            }

            return result;
        }

        public async Task<TModel> Single()
        {
            await EnsureRowEventListenersRegisteredAsync();
            return await Conn.RunOnActorAsync(() => Table.Iter().Select(ToModel).Single());
        }

        public async Task<TModel> SingleOrDefault()
        {
            await EnsureRowEventListenersRegisteredAsync();
            return await Conn.RunOnActorAsync(() => Table.Iter().Select(ToModel).SingleOrDefault());
        }

        /// <summary>
        /// Runs a read against the locally subscribed cache, materializing the result before it
        /// leaves the actor thread - for a leaf repository's own bespoke predicate finders (e.g.
        /// SpacetimeTagRepository.FindByLabel), which used to build a RemoteQuery WHERE fragment
        /// and now query Table.Iter() (or a generated index handle) directly instead. Never return
        /// a raw IEnumerable/Table.Iter() handle from the query delegate - it must not escape the
        /// actor thread, since enumerating it concurrently with FrameTick() is exactly the race
        /// this class exists to avoid.
        /// </summary>
        protected async Task<TResult> Query<TResult>(Func<RemoteTableHandle<EventContext, TStdbRow>, TResult> query)
        {
            await EnsureRowEventListenersRegisteredAsync();
            return await Conn.RunOnActorAsync(() => query(Table));
        }

        // Opt-in hook for the migration tool (Sonarr.SpacetimeMigration) cutting an existing
        // SQLite-backed install over to SpacetimeDB - unlike Insert(), which always assigns a
        // fresh id, this preserves model.Id exactly so foreign keys (Episode.SeriesId, etc.)
        // transfer as-is with no in-script id-remapping needed. Only overridden by the handful of
        // repositories the migration's first pass covers - everything else throws until the
        // migration's scope grows to match.
        public virtual Task MigrateInsert(TModel model) =>
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
        protected async Task InvokeAndWaitForMigrateInsert(int id, Action invokeReducer)
        {
            await EnsureRowEventListenersRegisteredAsync();

            await _writeLock.WaitAsync();

            try
            {
                Exception failure = null;
                var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void Handler(EventContext ctx, TStdbRow row)
                {
                    if (!IsOwnConnectionEvent(ctx) || GetRowId(row) != id)
                    {
                        return;
                    }

                    confirmedTcs.TrySetResult(true);
                }

                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmedTcs.TrySetResult(true);
                });

                await Conn.RunOnActorAsync(() => Table.OnInsert += Handler);

                try
                {
                    invokeReducer();

                    if (!await WaitForConfirmation(confirmedTcs))
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
                    await Conn.RunOnActorAsync(() => Table.OnInsert -= Handler);
                }
            }
            finally
            {
                _writeLock.Release();
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
        protected async Task InvokeAndWaitForReducerCommitted(Action invokeReducer, Func<Action, Action<Exception>, IDisposable> subscribeCommitted)
        {
            await _writeLock.WaitAsync();

            try
            {
                Exception failure = null;
                var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmedTcs.TrySetResult(true);
                });

                var subscription = await Conn.RunOnActorAsync(() => subscribeCommitted(
                    () => confirmedTcs.TrySetResult(true),
                    ex =>
                    {
                        failure = ex;
                        confirmedTcs.TrySetResult(true);
                    }));

                try
                {
                    invokeReducer();

                    if (!await WaitForConfirmation(confirmedTcs))
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
                    await Conn.RunOnActorAsync(() => subscription.Dispose());
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task<TModel> Insert(TModel model)
        {
            if (model.Id != 0)
            {
                throw new InvalidOperationException("Can't insert model with existing ID " + model.Id);
            }

            await EnsureRowEventListenersRegisteredAsync();

            await _writeLock.WaitAsync();

            try
            {
                TStdbRow insertedRow = null;
                Exception failure = null;
                var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void Handler(EventContext ctx, TStdbRow row)
                {
                    if (!IsOwnConnectionEvent(ctx))
                    {
                        return;
                    }

                    insertedRow = row;
                    confirmedTcs.TrySetResult(true);
                }

                // Registered before the row-event handler, not after: if the connection is lost in
                // the gap between the two, RegisterPendingOperation must already be listening so
                // HandleDisconnect's sweep of currently-pending operations doesn't miss this one
                // (a disconnect that lands in that gap would otherwise go unnoticed until the full
                // WriteConfirmationTimeout elapses instead of failing immediately).
                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmedTcs.TrySetResult(true);
                });

                // Registered (and later removed) exclusively via RunOnActorAsync, so it can never
                // race FrameTick()'s own invocation of this same event on the actor thread.
                // Registered before invoking the reducer, inside the same lock a different
                // Insert() call through this instance would also need - only one insert to this
                // table via this repository can be in flight at a time, so the first OnInsert
                // event we see that's confirmed as OUR OWN connection's doing (not a genuinely
                // different connection's insert into the same table, which fires this same event
                // but with a different CallerIdentity/CallerConnectionId, and is simply ignored)
                // has to be this call's row. No guessing which of several new ids is ours the way
                // a polling-based "grab the current max id" approach would have to.
                await Conn.RunOnActorAsync(() => Table.OnInsert += Handler);

                // Failure-fast-only channel: a rejected insert reducer (Status.Failed/
                // OutOfEnergy) would otherwise never produce a row event and would sit out the
                // full WriteConfirmationTimeout instead of failing in milliseconds. onCommitted
                // is deliberately a no-op - see SubscribeOwnInsertCommitted's remarks and this
                // class's own class-level doc comment for why success must only ever be signaled
                // by the OnInsert row event above (the sole source of the new row's id).
                var insertCommittedSubscription = await Conn.RunOnActorAsync(() => SubscribeOwnInsertCommitted(
                    onCommitted: () => { },
                    onFailed: ex =>
                    {
                        failure = ex;
                        confirmedTcs.TrySetResult(true);
                    }));

                try
                {
                    InvokeInsertReducer(model);

                    if (!await WaitForConfirmation(confirmedTcs))
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
                    await Conn.RunOnActorAsync(() =>
                    {
                        Table.OnInsert -= Handler;
                        insertCommittedSubscription.Dispose();
                    });
                }

                model.Id = GetRowId(insertedRow);

                // Runs while still holding _writeLock, with model.Id now correctly resolved -
                // the hook a subclass needing an additional reducer call keyed by the new row's
                // id (e.g. SpacetimeSeriesRepository replacing the new Series' tags) should use.
                await AfterInsertIdResolved(model);
            }
            finally
            {
                _writeLock.Release();
            }

            PublishModelEvent(model, ModelAction.Created);

            return model;
        }

        /// <summary>
        /// Extension point for a subclass that needs one more reducer call keyed by the newly
        /// assigned id as part of the same insert (e.g. a junction-table replace) - runs inside
        /// Insert's lock, after model.Id has been safely resolved. No-op by default.
        /// </summary>
        protected virtual Task AfterInsertIdResolved(TModel model) => Task.CompletedTask;

        // Bounded safety-net timeout for the confirmation waits below - the ordinary path
        // resolves as soon as the corresponding row/reducer event arrives, usually well under
        // this; this only matters if the reducer call fails silently or the connection is
        // unresponsive. Overridable purely so an integration test against a live server can
        // shrink it instead of waiting out the full default to exercise the timeout path -
        // production code never overrides this.
        protected virtual TimeSpan WriteConfirmationTimeout => TimeSpan.FromSeconds(5);

        // Awaits a write-confirmation TaskCompletionSource with WriteConfirmationTimeout applied,
        // returning false on timeout instead of letting Task.WaitAsync's TimeoutException
        // propagate - every call site below already has its own more descriptive
        // InvalidOperationException to throw on a false result, matching the pre-async
        // ManualResetEventSlim.Wait(timeout) callers were written against.
        private async Task<bool> WaitForConfirmation(TaskCompletionSource<bool> confirmedTcs)
        {
            try
            {
                return await confirmedTcs.Task.WaitAsync(WriteConfirmationTimeout);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

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

        // Gap fix: event ownership. Every write method below (Insert/Update/SetFields/Delete)
        // already publishes a ModelEvent<TModel> for its OWN confirmed write via an explicit
        // PublishModelEvent call after that write completes. That leaves a hole: if some OTHER
        // writer mutates this same table - a scheduled/bulk reducer running independently, a
        // different Sonarr instance sharing the database (not a real deployment topology today,
        // but architecturally possible), or a future migration/admin tool - the row changes in
        // this connection's local subscription cache (the generated Table.OnInsert/OnUpdate/
        // OnDelete events fire for ANY row change, not just ones this connection caused), but
        // nothing ever turns that into a ModelEvent<TModel>, since the only listeners on those
        // events used to be the transient per-write confirmation handlers that get registered
        // and torn down around each individual write.
        //
        // The fix is these three always-on handlers, registered once per repository instance
        // (see EnsureRowEventListenersRegisteredAsync) rather than per-write, and left
        // registered for the lifetime of the instance. Each checks IsOwnConnectionEvent and
        // returns early for a self-caused change - that case is already handled by the
        // explicit PublishModelEvent call the write method itself makes once its own write is
        // confirmed, and publishing here too would double-publish the same change. Only a
        // change this connection did NOT cause reaches PublishModelEvent from here - which is
        // exactly the case nothing previously published for.
        //
        // Registration lifecycle: repository instances are registered as DryIoc singletons (see
        // CompositionExtensions.Register, Reuse.Singleton) with no observed Dispose/teardown
        // path in this codebase's DI setup, so they live for the whole app lifetime. On that
        // basis, "register once at first use, never unregister" is treated as acceptable here,
        // not an oversight - if a shorter-lived repository lifetime is ever introduced, this
        // would need an IDisposable on the repository that runs
        // Conn.RunOnActor(() => { Table.OnInsert -= ...; Table.OnUpdate -= ...; Table.OnDelete -= ...; })
        // to unregister these three handlers.
        private void OnForeignRowInserted(EventContext ctx, TStdbRow row)
        {
            if (IsOwnConnectionEvent(ctx))
            {
                return;
            }

            PublishModelEvent(ToModel(row), ModelAction.Created);
        }

        private void OnForeignRowUpdated(EventContext ctx, TStdbRow oldRow, TStdbRow newRow)
        {
            if (IsOwnConnectionEvent(ctx))
            {
                return;
            }

            PublishModelEvent(ToModel(newRow), ModelAction.Updated);
        }

        private void OnForeignRowDeleted(EventContext ctx, TStdbRow row)
        {
            if (IsOwnConnectionEvent(ctx))
            {
                return;
            }

            PublishModelEvent(ToModel(row), ModelAction.Deleted);
        }

        // One-time (per repository instance) registration of the three always-on handlers
        // above. Called defensively from every public method that touches Table (reads and
        // writes alike) so the listeners come alive on this instance's first real use rather
        // than at construction time - Table is an abstract property a leaf subclass's
        // constructor may not have fully wired up yet, and eagerly touching it from this base
        // class's own constructor would also make it impossible to unit-test the Table-free
        // parts of this class in isolation (see SpacetimeBasicRepositoryPlumbingFixture, whose
        // TestableSpacetimeRepository deliberately throws from Table since it's never meant to
        // be touched by those tests). The volatile bool fast-paths every call after the first
        // without taking the lock; the lock only matters for the handful of calls racing to be
        // first.
        private async Task EnsureRowEventListenersRegisteredAsync()
        {
            if (_rowEventListenersRegistered)
            {
                return;
            }

            await _rowEventListenerRegistrationLock.WaitAsync();

            try
            {
                if (_rowEventListenersRegistered)
                {
                    return;
                }

                await Conn.RunOnActorAsync(() =>
                {
                    Table.OnInsert += OnForeignRowInserted;
                    Table.OnUpdate += OnForeignRowUpdated;
                    Table.OnDelete += OnForeignRowDeleted;
                });

                _rowEventListenersRegistered = true;
            }
            finally
            {
                _rowEventListenerRegistrationLock.Release();
            }
        }

        public async Task InsertMany(IList<TModel> models)
        {
            if (models.Any(x => x.Id != 0))
            {
                throw new InvalidOperationException("Can't insert model with existing ID != 0");
            }

            foreach (var model in models)
            {
                await Insert(model);
            }
        }

        public async Task<TModel> Update(TModel model)
        {
            if (model.Id == 0)
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            await EnsureRowEventListenersRegisteredAsync();

            await _writeLock.WaitAsync();

            try
            {
                await InvokeAndWaitForUpdate(model.Id, () => InvokeUpdateReducer(model));
            }
            finally
            {
                _writeLock.Release();
            }

            PublishModelEvent(model, ModelAction.Updated);

            return model;
        }

        public async Task UpdateMany(IList<TModel> models)
        {
            if (models.Any(x => x.Id == 0))
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            foreach (var model in models)
            {
                await Update(model);
            }
        }

        public async Task<TModel> Upsert(TModel model) => model.Id == 0 ? await Insert(model) : await Update(model);

        public async Task SetFields(TModel model, params Expression<Func<TModel, object>>[] properties)
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

            await EnsureRowEventListenersRegisteredAsync();

            await _writeLock.WaitAsync();

            try
            {
                current = await Get(model.Id);

                foreach (var property in properties.Select(p => p.GetMemberName()))
                {
                    property.SetValue(current, property.GetValue(model));
                }

                await InvokeAndWaitForUpdate(current.Id, () => InvokeUpdateReducer(current));
            }
            finally
            {
                _writeLock.Release();
            }

            PublishModelEvent(current, ModelAction.Updated);
        }

        // Shared by Update() and SetFields(): invokes the given reducer call and waits for this
        // entity's own reducer-committed result to confirm it landed for the specific row id
        // being updated - or throws immediately if that channel reports Status.Failed/
        // OutOfEnergy. This channel alone is sufficient: it already reports Status.Committed for
        // a genuine no-op update (byte-identical row content, which commits successfully
        // server-side but produces no row-delta event at all) - that's the entire reason this
        // channel exists rather than watching Table.OnUpdate - and by the time it fires, the
        // corresponding row-delta (if any) has already been applied to the local cache within the
        // same transaction processing cycle, so nothing is lost by not also watching the row
        // event. The handler is filtered to events this connection itself caused (see
        // IsOwnConnectionEvent, folded into SubscribeOwnUpdateCommitted's own implementation) for
        // the specific row id, not just "any update to this table", in case a different
        // connection updates a different row while this one is waiting. Caller must already hold
        // _writeLock.
        private async Task InvokeAndWaitForUpdate(int id, Action invokeReducer)
        {
            Exception failure = null;
            var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void CommittedHandler(int committedId)
            {
                if (committedId != id)
                {
                    return;
                }

                confirmedTcs.TrySetResult(true);
            }

            void FailureHandler(Exception ex)
            {
                failure = ex;
                confirmedTcs.TrySetResult(true);
            }

            using var pendingOp = Conn.RegisterPendingOperation(ex =>
            {
                failure = ex;
                confirmedTcs.TrySetResult(true);
            });

            var committedSubscription = await Conn.RunOnActorAsync(() => SubscribeOwnUpdateCommitted(CommittedHandler, FailureHandler));

            try
            {
                invokeReducer();

                if (!await WaitForConfirmation(confirmedTcs))
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
                await Conn.RunOnActorAsync(() => committedSubscription.Dispose());
            }
        }

        // One outer lock around the whole batch, not just each per-model SetFields' own inner
        // lock - _writeLock is a SemaphoreSlim(1,1), NOT re-entrant the way Monitor is, so
        // SetFields(IList<TModel>, ...) below cannot simply call the single-model SetFields (that
        // would deadlock trying to re-acquire a semaphore this same call already holds). It
        // duplicates the read-merge-write-and-wait sequence per model instead, all under one
        // WaitAsync/Release pair for the whole batch - closing the same gap the single-model
        // overload's own comment describes, without re-entering the lock.
        public async Task SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties)
        {
            await EnsureRowEventListenersRegisteredAsync();

            await _writeLock.WaitAsync();

            try
            {
                foreach (var model in models)
                {
                    var current = await Get(model.Id);

                    foreach (var property in properties.Select(p => p.GetMemberName()))
                    {
                        property.SetValue(current, property.GetValue(model));
                    }

                    await InvokeAndWaitForUpdate(current.Id, () => InvokeUpdateReducer(current));

                    PublishModelEvent(current, ModelAction.Updated);
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task Delete(TModel model) => Delete(model.Id);

        // Same pattern as InvokeAndWaitForUpdate above (see that method's remarks): confirmed
        // exclusively via this entity's own reducer-committed result, not a Table.OnDelete row
        // event. Delete doesn't need to read anything back off a row event the way Insert does
        // for its id (there's nothing left to read once the row is gone), and unlike Update there
        // is no legitimate "no-op delete" case to worry about either way - the reducer-committed
        // channel's Status.Committed/Status.Failed/OutOfEnergy dispatch is already a complete
        // confirmation signal on its own. Caller must already hold _writeLock.
        public async Task Delete(int id)
        {
            await EnsureRowEventListenersRegisteredAsync();

            await _writeLock.WaitAsync();

            try
            {
                Exception failure = null;
                var confirmedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                void CommittedHandler(int committedId)
                {
                    if (committedId != id)
                    {
                        return;
                    }

                    confirmedTcs.TrySetResult(true);
                }

                void FailureHandler(Exception ex)
                {
                    failure = ex;
                    confirmedTcs.TrySetResult(true);
                }

                using var pendingOp = Conn.RegisterPendingOperation(ex =>
                {
                    failure = ex;
                    confirmedTcs.TrySetResult(true);
                });

                var committedSubscription = await Conn.RunOnActorAsync(() => SubscribeOwnDeleteCommitted(CommittedHandler, FailureHandler));

                try
                {
                    InvokeDeleteReducer(id);

                    if (!await WaitForConfirmation(confirmedTcs))
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
                    await Conn.RunOnActorAsync(() => committedSubscription.Dispose());
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public Task DeleteMany(List<TModel> models) => DeleteMany(models.Select(m => m.Id));

        public async Task DeleteMany(IEnumerable<int> ids)
        {
            foreach (var id in ids)
            {
                await Delete(id);
            }
        }

        public async Task Purge(bool vacuum = false)
        {
            foreach (var model in (await All()).ToList())
            {
                await Delete(model.Id);
            }
        }

        public virtual async Task<PagingSpec<TModel>> GetPaged(PagingSpec<TModel> pagingSpec)
        {
            var query = (await All()).AsEnumerable();

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
