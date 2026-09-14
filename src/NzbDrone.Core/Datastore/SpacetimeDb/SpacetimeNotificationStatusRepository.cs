using System;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbNotificationStatus = SpacetimeDB.Types.NotificationStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeNotificationStatusRepository : SpacetimeProviderStatusRepository<NotificationStatus, StdbNotificationStatus>, INotificationStatusRepository
    {
        public SpacetimeNotificationStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbNotificationStatus> Table => Conn.Connection.Db.NotificationStatus;

        protected override StdbNotificationStatus FindRowById(int id) => Conn.Connection.Db.NotificationStatus.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, SpacetimeDB.Timestamp? p3, SpacetimeDB.Timestamp? p4, int p5, SpacetimeDB.Timestamp? p6)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateNotificationStatus += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateNotificationStatus -= Handler);
        }

        protected override NotificationStatus ToModel(StdbNotificationStatus row) => new NotificationStatus
        {
            Id = row.Id,
            ProviderId = row.ProviderId,
            InitialFailure = SpacetimeDateTime.ToDateTime(row.InitialFailure),
            MostRecentFailure = SpacetimeDateTime.ToDateTime(row.MostRecentFailure),
            EscalationLevel = row.EscalationLevel,
            DisabledTill = SpacetimeDateTime.ToDateTime(row.DisabledTill)
        };

        protected override int GetRowId(StdbNotificationStatus row) => row.Id;

        protected override void InvokeInsertReducer(NotificationStatus model) => Conn.Connection.Reducers.InsertNotificationStatus(
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeUpdateReducer(NotificationStatus model) => Conn.Connection.Reducers.UpdateNotificationStatus(
            model.Id,
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteNotificationStatus(id);
    }
}
