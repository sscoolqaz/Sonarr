using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using StdbRootFolder = SpacetimeDB.Types.RootFolder;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeRootFolderRepository : SpacetimeBasicRepository<RootFolder, StdbRootFolder>, IRootFolderRepository
    {
        public SpacetimeRootFolderRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override bool PublishModelEvents => true;

        protected override StdbRootFolder[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.RootFolder.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override RootFolder ToModel(StdbRootFolder row) => new RootFolder { Id = row.Id, Path = row.Path };

        protected override int GetRowId(StdbRootFolder row) => row.Id;

        protected override void InvokeInsertReducer(RootFolder model) => Conn.Connection.Reducers.InsertRootFolder(model.Path);

        protected override void InvokeUpdateReducer(RootFolder model) => Conn.Connection.Reducers.UpdateRootFolder(model.Id, model.Path);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteRootFolder(id);
    }
}
