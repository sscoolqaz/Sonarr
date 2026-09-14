using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Update.History;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbUpdateHistory = SpacetimeDB.Types.UpdateHistory;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeUpdateHistoryRepository : SpacetimeBasicRepository<UpdateHistory, StdbUpdateHistory>, IUpdateHistoryRepository
    {
        public SpacetimeUpdateHistoryRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbUpdateHistory> Table => Conn.Connection.Db.UpdateHistory;

        protected override StdbUpdateHistory FindRowById(int id) => Conn.Connection.Db.UpdateHistory.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, SpacetimeDB.Timestamp p2, string p3, int p4)
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

            Conn.Connection.Reducers.OnUpdateUpdateHistory += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateUpdateHistory -= Handler);
        }

        protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id)
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

            Conn.Connection.Reducers.OnDeleteUpdateHistory += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteUpdateHistory -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, SpacetimeDB.Timestamp p1, string p2, int p3)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted();
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnInsertUpdateHistory += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertUpdateHistory -= Handler);
        }

        protected override UpdateHistory ToModel(StdbUpdateHistory row) => new UpdateHistory
        {
            Id = row.Id,
            Date = SpacetimeDateTime.ToDateTime(row.Date),
            Version = Version.Parse(row.Version),
            EventType = (UpdateHistoryEventType)row.EventType
        };

        protected override int GetRowId(StdbUpdateHistory row) => row.Id;

        protected override void InvokeInsertReducer(UpdateHistory model) => Conn.Connection.Reducers.InsertUpdateHistory(
            SpacetimeDateTime.ToTimestamp(model.Date), model.Version.ToString(), (int)model.EventType);

        public override Task MigrateInsert(UpdateHistory model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertUpdateHistory(
            model.Id, SpacetimeDateTime.ToTimestamp(model.Date), model.Version.ToString(), (int)model.EventType));

        protected override void InvokeUpdateReducer(UpdateHistory model) => Conn.Connection.Reducers.UpdateUpdateHistory(
            model.Id, SpacetimeDateTime.ToTimestamp(model.Date), model.Version.ToString(), (int)model.EventType);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteUpdateHistory(id);

        public async Task<UpdateHistory> LastInstalled() =>
            (await All()).Where(v => v.EventType == UpdateHistoryEventType.Installed).OrderByDescending(v => v.Date).Take(1).FirstOrDefault();

        public async Task<UpdateHistory> PreviouslyInstalled() =>
            (await All()).Where(v => v.EventType == UpdateHistoryEventType.Installed).OrderByDescending(v => v.Date).Skip(1).Take(1).FirstOrDefault();

        public async Task<List<UpdateHistory>> InstalledSince(DateTime dateTime) =>
            (await All()).Where(v => v.EventType == UpdateHistoryEventType.Installed && v.Date >= dateTime).OrderBy(v => v.Date).ToList();
    }
}
