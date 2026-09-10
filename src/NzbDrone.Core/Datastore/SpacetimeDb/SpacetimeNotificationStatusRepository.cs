using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using StdbNotificationStatus = SpacetimeDB.Types.NotificationStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeNotificationStatusRepository : SpacetimeProviderStatusRepository<NotificationStatus, StdbNotificationStatus>, INotificationStatusRepository
    {
        public SpacetimeNotificationStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbNotificationStatus[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.NotificationStatus.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

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
