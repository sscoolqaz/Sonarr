using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Messaging.Events;
using StdbImportListStatus = SpacetimeDB.Types.ImportListStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeImportListStatusRepository : SpacetimeProviderStatusRepository<ImportListStatus, StdbImportListStatus>, IImportListStatusRepository
    {
        public SpacetimeImportListStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbImportListStatus[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.ImportListStatus.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override ImportListStatus ToModel(StdbImportListStatus row) => new ImportListStatus
        {
            Id = row.Id,
            ProviderId = row.ProviderId,
            InitialFailure = SpacetimeDateTime.ToDateTime(row.InitialFailure),
            MostRecentFailure = SpacetimeDateTime.ToDateTime(row.MostRecentFailure),
            EscalationLevel = row.EscalationLevel,
            DisabledTill = SpacetimeDateTime.ToDateTime(row.DisabledTill)
        };

        protected override int GetRowId(StdbImportListStatus row) => row.Id;

        protected override void InvokeInsertReducer(ImportListStatus model) => Conn.Connection.Reducers.InsertImportListStatus(
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeUpdateReducer(ImportListStatus model) => Conn.Connection.Reducers.UpdateImportListStatus(
            model.Id,
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteImportListStatus(id);
    }
}
