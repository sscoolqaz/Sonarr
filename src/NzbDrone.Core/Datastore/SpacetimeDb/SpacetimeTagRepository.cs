using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using NzbDrone.Core.Tags;
using StdbTag = SpacetimeDB.Types.Tag;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Phase 1 spike: a SpacetimeDB-backed implementation of ITagRepository, proving that
    /// IBasicRepository&lt;T&gt;'s synchronous surface can be bridged onto SpacetimeDB's
    /// reducer/subscription model without changing callers (TagService, TagController).
    ///
    /// Design notes (see plan's cross-cutting risk register for the full writeup):
    /// - Reads go through SpacetimeDB's awaitable RemoteQuery, blocked synchronously via
    ///   GetAwaiter().GetResult(). This resolves on the SDK's own background network thread
    ///   and does NOT require the FrameTick pump - confirmed empirically reliable
    ///   (insert-then-immediate-query is consistent, ~2ms) in the Phase 1 spike harness.
    /// - Writes (Insert/Update/Delete) are serialized with a lock, because reducer
    ///   completion is signalled via connection-wide events (OnInsertTag, etc.) that are not
    ///   correlated to a specific call. Serializing writes on this connection makes
    ///   "the newest row matching what I just wrote" unambiguous without needing a
    ///   client-supplied correlation id column on every table (a real design decision for
    ///   Phase 3, deferred here since Tag's write volume is low).
    /// - The FrameTick pump thread is still required for reducer error callbacks
    ///   (OnUnhandledReducerError) to fire; it runs for the lifetime of the repository.
    /// </summary>
    public class SpacetimeTagRepository : ITagRepository, IDisposable
    {
        private readonly SpacetimeDB.Types.DbConnection _conn;
        private readonly Thread _pumpThread;
        private readonly CancellationTokenSource _pumpCts = new CancellationTokenSource();
        private readonly object _writeLock = new object();

        public SpacetimeTagRepository(string host, string databaseName)
        {
            var connectedTcs = new TaskCompletionSource<SpacetimeDB.Types.DbConnection>();

            _conn = SpacetimeDB.Types.DbConnection.Builder()
                .WithUri(host)
                .WithDatabaseName(databaseName)
                .OnConnect((c, identity, token) => connectedTcs.TrySetResult(c))
                .OnConnectError(err => connectedTcs.TrySetException(err))
                .OnDisconnect((c, err) => { })
                .Build();

            _pumpThread = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "SpacetimeDB-FrameTick-Pump-Tag"
            };
            _pumpThread.Start();

            connectedTcs.Task.GetAwaiter().GetResult();
        }

        private void PumpLoop()
        {
            while (!_pumpCts.IsCancellationRequested)
            {
                _conn.FrameTick();
                Thread.Sleep(20);
            }
        }

        private static string EscapeSqlString(string value) => value.Replace("'", "''");

        private List<Tag> QueryWhere(string whereClauseWithoutPrefix)
        {
            var rows = _conn.Db.Tag.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();
            return rows.Select(ToSonarrTag).ToList();
        }

        private static Tag ToSonarrTag(StdbTag row) => new Tag { Id = row.Id, Label = row.Label };

        public IEnumerable<Tag> All() => QueryWhere(string.Empty);

        public int Count() => All().Count();

        public bool HasItems() => All().Any();

        public Tag Find(int id) => QueryWhere($"WHERE Id = {id}").FirstOrDefault();

        public Tag Get(int id)
        {
            var model = Find(id);

            if (model == null)
            {
                throw new ModelNotFoundException(typeof(Tag), id);
            }

            return model;
        }

        public IEnumerable<Tag> Get(IEnumerable<int> ids)
        {
            var idList = ids.ToList();

            if (!idList.Any())
            {
                return Array.Empty<Tag>();
            }

            var result = QueryWhere($"WHERE Id IN ({string.Join(",", idList)})");

            if (result.Count != idList.Count)
            {
                throw new ApplicationException($"Expected query to return {idList.Count} rows but returned {result.Count}");
            }

            return result;
        }

        public Tag GetByLabel(string label)
        {
            var model = FindByLabel(label);

            if (model == null)
            {
                throw new InvalidOperationException("Didn't find tag with label " + label);
            }

            return model;
        }

        public Tag FindByLabel(string label) => QueryWhere($"WHERE Label = '{EscapeSqlString(label)}'").SingleOrDefault();

        public List<Tag> GetTags(HashSet<int> tagIds) => All().Where(t => tagIds.Contains(t.Id)).ToList();

        public Tag Insert(Tag model)
        {
            if (model.Id != 0)
            {
                throw new InvalidOperationException("Can't insert model with existing ID " + model.Id);
            }

            lock (_writeLock)
            {
                _conn.Reducers.InsertTag(model.Label);

                // Writes are serialized on this connection (see class remarks), so the newest
                // row matching this label is unambiguously the one this call just created.
                var inserted = QueryWhere($"WHERE Label = '{EscapeSqlString(model.Label)}'")
                    .OrderByDescending(t => t.Id)
                    .First();

                model.Id = inserted.Id;
            }

            return model;
        }

        public Tag Update(Tag model)
        {
            if (model.Id == 0)
            {
                throw new InvalidOperationException("Can't update model with ID 0");
            }

            lock (_writeLock)
            {
                _conn.Reducers.UpdateTag(model.Id, model.Label);
            }

            return model;
        }

        public Tag Upsert(Tag model) => model.Id == 0 ? Insert(model) : Update(model);

        public void SetFields(Tag model, params Expression<Func<Tag, object>>[] properties)
        {
            // Tag only has Label beyond Id, so a partial-field update is equivalent to a full update.
            Update(model);
        }

        public void Delete(Tag model) => Delete(model.Id);

        public void Delete(int id)
        {
            lock (_writeLock)
            {
                _conn.Reducers.DeleteTag(id);
            }
        }

        public void InsertMany(IList<Tag> models)
        {
            foreach (var model in models)
            {
                Insert(model);
            }
        }

        public void UpdateMany(IList<Tag> models)
        {
            foreach (var model in models)
            {
                Update(model);
            }
        }

        public void SetFields(IList<Tag> models, params Expression<Func<Tag, object>>[] properties)
        {
            foreach (var model in models)
            {
                SetFields(model, properties);
            }
        }

        public void DeleteMany(List<Tag> models)
        {
            foreach (var model in models)
            {
                Delete(model);
            }
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
            foreach (var tag in All().ToList())
            {
                Delete(tag.Id);
            }
        }

        public Tag Single() => All().Single();

        public Tag SingleOrDefault() => All().SingleOrDefault();

        public PagingSpec<Tag> GetPaged(PagingSpec<Tag> pagingSpec)
        {
            var query = All().AsEnumerable();

            foreach (var filter in pagingSpec.FilterExpressions)
            {
                query = query.Where(filter.Compile());
            }

            var allMatching = query.ToList();
            pagingSpec.TotalRecords = allMatching.Count;

            var sorted = pagingSpec.SortDirection == SortDirection.Descending
                ? allMatching.OrderByDescending(t => t.Label)
                : allMatching.OrderBy(t => t.Label);

            pagingSpec.Records = sorted
                .Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize)
                .Take(pagingSpec.PageSize)
                .ToList();

            return pagingSpec;
        }

        public void Dispose()
        {
            _pumpCts.Cancel();
            _conn.Disconnect();
        }
    }
}
