using System;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
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

        protected override RemoteTableHandle<EventContext, StdbRootFolder> Table => Conn.Connection.Db.RootFolder;

        protected override StdbRootFolder FindRowById(int id) => Conn.Connection.Db.RootFolder.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2)
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

            Conn.Connection.Reducers.OnUpdateRootFolder += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateRootFolder -= Handler);
        }

        protected override RootFolder ToModel(StdbRootFolder row) => new RootFolder { Id = row.Id, Path = row.Path };

        protected override int GetRowId(StdbRootFolder row) => row.Id;

        protected override void InvokeInsertReducer(RootFolder model) => Conn.Connection.Reducers.InsertRootFolder(model.Path ?? string.Empty);

        public override Task MigrateInsert(RootFolder model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertRootFolder(model.Id, model.Path ?? string.Empty));

        protected override void InvokeUpdateReducer(RootFolder model) => Conn.Connection.Reducers.UpdateRootFolder(model.Id, model.Path ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteRootFolder(id);
    }
}
