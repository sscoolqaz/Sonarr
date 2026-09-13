using System.Linq;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Events;
using StdbImportListExclusion = SpacetimeDB.Types.ImportListExclusion;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeImportListExclusionRepository : SpacetimeBasicRepository<ImportListExclusion, StdbImportListExclusion>, IImportListExclusionRepository
    {
        public SpacetimeImportListExclusionRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbImportListExclusion[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.ImportListExclusion.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override ImportListExclusion ToModel(StdbImportListExclusion row) =>
            new ImportListExclusion { Id = row.Id, TvdbId = row.TvdbId, Title = row.Title };

        protected override int GetRowId(StdbImportListExclusion row) => row.Id;

        protected override void InvokeInsertReducer(ImportListExclusion model) =>
            Conn.Connection.Reducers.InsertImportListExclusion(model.TvdbId, model.Title);

        public override void MigrateInsert(ImportListExclusion model) =>
            Conn.Connection.Reducers.MigrateInsertImportListExclusion(model.Id, model.TvdbId, model.Title);

        protected override void InvokeUpdateReducer(ImportListExclusion model) =>
            Conn.Connection.Reducers.UpdateImportListExclusion(model.Id, model.TvdbId, model.Title);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteImportListExclusion(id);

        public ImportListExclusion FindByTvdbId(int tvdbId) =>
            RemoteQuery($"WHERE TvdbId = {tvdbId}").Select(ToModel).SingleOrDefault();
    }
}
