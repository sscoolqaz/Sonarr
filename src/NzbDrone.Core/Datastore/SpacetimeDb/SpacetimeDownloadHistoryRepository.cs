using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using StdbDownloadHistory = SpacetimeDB.Types.DownloadHistory;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDownloadHistoryRepository : SpacetimeBasicRepository<DownloadHistory, StdbDownloadHistory>, IDownloadHistoryRepository
    {
        public SpacetimeDownloadHistoryRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbDownloadHistory[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.DownloadHistory.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override DownloadHistory ToModel(StdbDownloadHistory row) => new DownloadHistory
        {
            Id = row.Id,
            EventType = (DownloadHistoryEventType)row.EventType,
            SeriesId = row.SeriesId,
            DownloadId = row.DownloadId,
            SourceTitle = row.SourceTitle,
            Date = SpacetimeDateTime.ToDateTime(row.Date),
            Protocol = (DownloadProtocol)row.Protocol,
            IndexerId = row.IndexerId,
            DownloadClientId = row.DownloadClientId,
            Release = SpacetimeJson.Deserialize<ReleaseInfo>(row.ReleaseJson),
            Data = SpacetimeJson.Deserialize<Dictionary<string, string>>(row.DataJson) ?? new Dictionary<string, string>()
        };

        protected override int GetRowId(StdbDownloadHistory row) => row.Id;

        protected override void InvokeInsertReducer(DownloadHistory model) => Conn.Connection.Reducers.InsertDownloadHistory(
            (int)model.EventType,
            model.SeriesId,
            model.DownloadId ?? string.Empty,
            model.SourceTitle ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Date),
            (int)model.Protocol,
            model.IndexerId,
            model.DownloadClientId,
            SpacetimeJson.Serialize(model.Release),
            SpacetimeJson.Serialize(model.Data));

        public override void MigrateInsert(DownloadHistory model) => Conn.Connection.Reducers.MigrateInsertDownloadHistory(
            model.Id,
            (int)model.EventType,
            model.SeriesId,
            model.DownloadId ?? string.Empty,
            model.SourceTitle ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Date),
            (int)model.Protocol,
            model.IndexerId,
            model.DownloadClientId,
            SpacetimeJson.Serialize(model.Release),
            SpacetimeJson.Serialize(model.Data));

        protected override void InvokeUpdateReducer(DownloadHistory model) => Conn.Connection.Reducers.UpdateDownloadHistory(
            model.Id,
            (int)model.EventType,
            model.SeriesId,
            model.DownloadId ?? string.Empty,
            model.SourceTitle ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Date),
            (int)model.Protocol,
            model.IndexerId,
            model.DownloadClientId,
            SpacetimeJson.Serialize(model.Release),
            SpacetimeJson.Serialize(model.Data));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteDownloadHistory(id);

        public List<DownloadHistory> FindByDownloadId(string downloadId) =>
            RemoteQuery($"WHERE DownloadId = '{EscapeSqlString(downloadId)}'").Select(ToModel).OrderByDescending(h => h.Date).ToList();

        public void DeleteBySeriesIds(List<int> seriesIds)
        {
            foreach (var row in All().Where(h => seriesIds.Contains(h.SeriesId)).ToList())
            {
                Delete(row.Id);
            }
        }
    }
}
