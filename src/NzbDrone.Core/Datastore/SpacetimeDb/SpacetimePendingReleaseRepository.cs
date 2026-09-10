using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using StdbPendingRelease = SpacetimeDB.Types.PendingRelease;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimePendingReleaseRepository : SpacetimeBasicRepository<PendingRelease, StdbPendingRelease>, IPendingReleaseRepository
    {
        public SpacetimePendingReleaseRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbPendingRelease[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.PendingRelease.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override PendingRelease ToModel(StdbPendingRelease row) => new PendingRelease
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            Title = row.Title,
            Added = SpacetimeDateTime.ToDateTime(row.Added),
            ParsedEpisodeInfo = SpacetimeJson.Deserialize<ParsedEpisodeInfo>(row.ParsedEpisodeInfoJson),
            Release = SpacetimeJson.Deserialize<ReleaseInfo>(row.ReleaseJson),
            Reason = (PendingReleaseReason)row.Reason,
            AdditionalInfo = SpacetimeJson.Deserialize<PendingReleaseAdditionalInfo>(row.AdditionalInfoJson)
        };

        protected override int GetRowId(StdbPendingRelease row) => row.Id;

        protected override void InvokeInsertReducer(PendingRelease model) => Conn.Connection.Reducers.InsertPendingRelease(
            model.SeriesId,
            model.Title ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeJson.Serialize(model.ParsedEpisodeInfo),
            SpacetimeJson.Serialize(model.Release),
            (int)model.Reason,
            SpacetimeJson.Serialize(model.AdditionalInfo));

        protected override void InvokeUpdateReducer(PendingRelease model) => Conn.Connection.Reducers.UpdatePendingRelease(
            model.Id,
            model.SeriesId,
            model.Title ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeJson.Serialize(model.ParsedEpisodeInfo),
            SpacetimeJson.Serialize(model.Release),
            (int)model.Reason,
            SpacetimeJson.Serialize(model.AdditionalInfo));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeletePendingRelease(id);

        public void DeleteBySeriesIds(List<int> seriesIds)
        {
            foreach (var row in All().Where(r => seriesIds.Contains(r.SeriesId)).ToList())
            {
                Delete(row.Id);
            }
        }

        public List<PendingRelease> AllBySeriesId(int seriesId) =>
            RemoteQuery($"WHERE SeriesId = {seriesId}").Select(ToModel).ToList();

        // No production caller (confirmed by the Phase 3 adversarial review) and the join it did
        // in SQL projected no columns from Series anyway - a plain filter is equivalent.
        public List<PendingRelease> WithoutFallback() =>
            All().Where(r => r.Reason != PendingReleaseReason.Fallback).ToList();
    }
}
