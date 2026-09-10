using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using StdbDownloadClientStatus = SpacetimeDB.Types.DownloadClientStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDownloadClientStatusRepository : SpacetimeProviderStatusRepository<DownloadClientStatus, StdbDownloadClientStatus>, IDownloadClientStatusRepository
    {
        public SpacetimeDownloadClientStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbDownloadClientStatus[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.DownloadClientStatus.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override DownloadClientStatus ToModel(StdbDownloadClientStatus row) => new DownloadClientStatus
        {
            Id = row.Id,
            ProviderId = row.ProviderId,
            InitialFailure = SpacetimeDateTime.ToDateTime(row.InitialFailure),
            MostRecentFailure = SpacetimeDateTime.ToDateTime(row.MostRecentFailure),
            EscalationLevel = row.EscalationLevel,
            DisabledTill = SpacetimeDateTime.ToDateTime(row.DisabledTill)
        };

        protected override int GetRowId(StdbDownloadClientStatus row) => row.Id;

        protected override void InvokeInsertReducer(DownloadClientStatus model) => Conn.Connection.Reducers.InsertDownloadClientStatus(
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeUpdateReducer(DownloadClientStatus model) => Conn.Connection.Reducers.UpdateDownloadClientStatus(
            model.Id,
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteDownloadClientStatus(id);
    }
}
