using System;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RemotePathMappings;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
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

        protected override RemoteTableHandle<EventContext, StdbRemotePathMapping> Table => Conn.Connection.Db.RemotePathMapping;

        protected override StdbRemotePathMapping FindRowById(int id) => Conn.Connection.Db.RemotePathMapping.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnUpdateRemotePathMapping += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateRemotePathMapping -= Handler);
        }

        protected override RemotePathMapping ToModel(StdbRemotePathMapping row) =>
            new RemotePathMapping { Id = row.Id, Host = row.Host, RemotePath = row.RemotePath, LocalPath = row.LocalPath };

        protected override int GetRowId(StdbRemotePathMapping row) => row.Id;

        protected override void InvokeInsertReducer(RemotePathMapping model) =>
            Conn.Connection.Reducers.InsertRemotePathMapping(model.Host ?? string.Empty, model.RemotePath ?? string.Empty, model.LocalPath ?? string.Empty);

        public override Task MigrateInsert(RemotePathMapping model) =>
            InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertRemotePathMapping(model.Id, model.Host ?? string.Empty, model.RemotePath ?? string.Empty, model.LocalPath ?? string.Empty));

        protected override void InvokeUpdateReducer(RemotePathMapping model) =>
            Conn.Connection.Reducers.UpdateRemotePathMapping(model.Id, model.Host ?? string.Empty, model.RemotePath ?? string.Empty, model.LocalPath ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteRemotePathMapping(id);
    }
}
