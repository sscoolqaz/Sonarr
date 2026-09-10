using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RemotePathMappings;
using StdbRemotePathMapping = SpacetimeDB.Types.RemotePathMapping;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeRemotePathMappingRepository : SpacetimeBasicRepository<RemotePathMapping, StdbRemotePathMapping>, IRemotePathMappingRepository
    {
        public SpacetimeRemotePathMappingRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override bool PublishModelEvents => true;

        protected override StdbRemotePathMapping[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.RemotePathMapping.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override RemotePathMapping ToModel(StdbRemotePathMapping row) =>
            new RemotePathMapping { Id = row.Id, Host = row.Host, RemotePath = row.RemotePath, LocalPath = row.LocalPath };

        protected override int GetRowId(StdbRemotePathMapping row) => row.Id;

        protected override void InvokeInsertReducer(RemotePathMapping model) =>
            Conn.Connection.Reducers.InsertRemotePathMapping(model.Host ?? string.Empty, model.RemotePath ?? string.Empty, model.LocalPath ?? string.Empty);

        protected override void InvokeUpdateReducer(RemotePathMapping model) =>
            Conn.Connection.Reducers.UpdateRemotePathMapping(model.Id, model.Host ?? string.Empty, model.RemotePath ?? string.Empty, model.LocalPath ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteRemotePathMapping(id);
    }
}
