using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Generic SpacetimeDB-backed implementation of IBasicRepository&lt;TModel&gt;, replacing
    /// BasicRepository&lt;TModel&gt; the same way SpacetimeTagRepository did by hand in Phase 1 -
    /// this is that pattern generalized so porting each of Phase 3's ~30 Tier 1 entities is a
    /// small subclass, not a fresh implementation of the whole interface every time.
    ///
    /// TStdbRow is the entity's generated SpacetimeDB row type (e.g. SpacetimeDB.Types.RootFolder)
    /// - concrete subclasses supply the handful of entity-specific pieces (the query/reducer
    /// delegates) that can't be generalized because each generated table is its own type.
    ///
    /// Write correlation (generalized from Phase 1's Label-based approach, which only worked
    /// because Tag happens to have a natural-ish unique field): writes are serialized per
    /// repository instance with a lock, so "the newest row by Id after this insert" is
    /// unambiguous regardless of the entity's other fields - no natural unique key needed. This
    /// also means only one entity's table is blocked at a time; a different entity's repository
    /// (sharing the same underlying connection) writes independently.
    /// </summary>
    public abstract class SpacetimeBasicRepository<TModel, TStdbRow> : IBasicRepository<TModel>
        where TModel : ModelBase, new()
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
        /// Runs `SELECT &lt;Table&gt;.* FROM &lt;Table&gt; {whereClauseWithoutPrefix}` against the
        /// live SpacetimeDB connection (see SpacetimeTagRepository's original remarks: this
        /// resolves on the SDK's own background network thread, independent of the FrameTick
        /// pump, and reliably reflects this connection's own prior writes immediately).
        /// </summary>
        protected abstract TStdbRow[] RemoteQuery(string whereClauseWithoutPrefix);

        protected abstract TModel ToModel(TStdbRow row);

        protected abstract int GetRowId(TStdbRow row);

        protected abstract void InvokeInsertReducer(TModel model);

        protected abstract void InvokeUpdateReducer(TModel model);

        protected abstract void InvokeDeleteReducer(int id);

        protected virtual bool PublishModelEvents => false;

        private List<TModel> Query(string whereClauseWithoutPrefix) =>
            RemoteQuery(whereClauseWithoutPrefix).Select(ToModel).ToList();

        public virtual IEnumerable<TModel> All() => Query(string.Empty);

        public int Count() => All().Count();

        public bool HasItems() => All().Any();

        public TModel Find(int id) => Query($"WHERE Id = {id}").FirstOrDefault();

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
            var idList = ids.ToList();

            if (!idList.Any())
            {
                return Array.Empty<TModel>();
            }

            var result = Query($"WHERE {IdOrChain(idList)}");

            if (result.Count != idList.Count)
            {
                throw new ApplicationException($"Expected query to return {idList.Count} rows but returned {result.Count}");
            }

            return result;
        }

        public TModel Single() => All().Single();

        public TModel SingleOrDefault() => All().SingleOrDefault();

        public TModel Insert(TModel model)
        {
            if (model.Id != 0)
            {
                throw new InvalidOperationException("Can't insert model with existing ID " + model.Id);
            }

            lock (_writeLock)
            {
                InvokeInsertReducer(model);

                // SpacetimeDB 2.10.0's ad-hoc SQL does NOT support ORDER BY/LIMIT despite the
                // docs implying it does - confirmed empirically (Unsupported: ... ORDER BY ...
                // LIMIT ..., HTTP 400). Fetch the whole table and pick the max Id client-side
                // instead - correct given writes are serialized by this lock, and cheap given
                // Tier 1/1.5 table sizes.
                var inserted = RemoteQuery(string.Empty).Select(ToModel).OrderByDescending(m => m.Id).First();
                model.Id = inserted.Id;
            }

            PublishModelEvent(model, ModelAction.Created);

            return model;
        }

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
                InvokeUpdateReducer(model);
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

            lock (_writeLock)
            {
                InvokeUpdateReducer(model);
            }

            PublishModelEvent(model, ModelAction.Updated);
        }

        public void SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties)
        {
            foreach (var model in models)
            {
                SetFields(model, properties);
            }
        }

        public void Delete(TModel model) => Delete(model.Id);

        public void Delete(int id)
        {
            lock (_writeLock)
            {
                InvokeDeleteReducer(id);
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

        protected static string EscapeSqlString(string value) => value?.Replace("'", "''");

        private static string IdOrChain(IEnumerable<int> ids) => string.Join(" OR ", ids.Select(id => $"Id = {id}"));

        private void PublishModelEvent(TModel model, ModelAction action, bool forcePublish = false)
        {
            if (PublishModelEvents || forcePublish)
            {
                _eventAggregator.PublishEvent(new ModelEvent<TModel>(model, action));
            }
        }
    }
}
