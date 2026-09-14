using System;
using System.Linq;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbImportListExclusion = SpacetimeDB.Types.ImportListExclusion;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeImportListExclusionRepository : SpacetimeBasicRepository<ImportListExclusion, StdbImportListExclusion>, IImportListExclusionRepository
    {
        public SpacetimeImportListExclusionRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbImportListExclusion> Table => Conn.Connection.Db.ImportListExclusion;

        protected override StdbImportListExclusion FindRowById(int id) => Conn.Connection.Db.ImportListExclusion.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, string p3)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateImportListExclusion += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateImportListExclusion -= Handler);
        }

        protected override ImportListExclusion ToModel(StdbImportListExclusion row) =>
            new ImportListExclusion { Id = row.Id, TvdbId = row.TvdbId, Title = row.Title };

        protected override int GetRowId(StdbImportListExclusion row) => row.Id;

        protected override void InvokeInsertReducer(ImportListExclusion model) =>
            Conn.Connection.Reducers.InsertImportListExclusion(model.TvdbId, model.Title);

        public override void MigrateInsert(ImportListExclusion model) =>
            InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertImportListExclusion(model.Id, model.TvdbId, model.Title));

        protected override void InvokeUpdateReducer(ImportListExclusion model) =>
            Conn.Connection.Reducers.UpdateImportListExclusion(model.Id, model.TvdbId, model.Title);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteImportListExclusion(id);

        public ImportListExclusion FindByTvdbId(int tvdbId) =>
            Query(t => t.Iter().Where(r => r.TvdbId == tvdbId).Select(ToModel).SingleOrDefault());
    }
}
