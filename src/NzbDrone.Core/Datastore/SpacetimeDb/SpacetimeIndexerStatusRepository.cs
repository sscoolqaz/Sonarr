using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using StdbIndexerStatus = SpacetimeDB.Types.IndexerStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeIndexerStatusRepository : SpacetimeProviderStatusRepository<IndexerStatus, StdbIndexerStatus>, IIndexerStatusRepository
    {
        public SpacetimeIndexerStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbIndexerStatus[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.IndexerStatus.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override IndexerStatus ToModel(StdbIndexerStatus row) => new IndexerStatus
        {
            Id = row.Id,
            ProviderId = row.ProviderId,
            InitialFailure = SpacetimeDateTime.ToDateTime(row.InitialFailure),
            MostRecentFailure = SpacetimeDateTime.ToDateTime(row.MostRecentFailure),
            EscalationLevel = row.EscalationLevel,
            DisabledTill = SpacetimeDateTime.ToDateTime(row.DisabledTill),
            LastRssSyncReleaseInfo = SpacetimeJson.Deserialize<ReleaseInfo>(row.LastRssSyncReleaseInfoJson)
        };

        protected override int GetRowId(StdbIndexerStatus row) => row.Id;

        protected override void InvokeInsertReducer(IndexerStatus model) => Conn.Connection.Reducers.InsertIndexerStatus(
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill),
            SpacetimeJson.Serialize(model.LastRssSyncReleaseInfo));

        protected override void InvokeUpdateReducer(IndexerStatus model) => Conn.Connection.Reducers.UpdateIndexerStatus(
            model.Id,
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill),
            SpacetimeJson.Serialize(model.LastRssSyncReleaseInfo));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteIndexerStatus(id);
    }
}
