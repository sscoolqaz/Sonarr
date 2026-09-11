using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using StdbBlocklist = SpacetimeDB.Types.Blocklist;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Same QualityId derivation invariant as SpacetimeHistoryRepository/
    /// SpacetimeMediaFileRepository - derived from the submitted Quality document at exactly one
    /// choke point, never accepted independently from a caller.
    /// </summary>
    public class SpacetimeBlocklistRepository : SpacetimeBasicRepository<Blocklist, StdbBlocklist>, IBlocklistRepository
    {
        private readonly ISeriesRepository _seriesRepository;
        private readonly IQualityProfileRankRepository _qualityRankRepository;

        public SpacetimeBlocklistRepository(
            ISpacetimeDbConnection connection,
            IEventAggregator eventAggregator,
            ISeriesRepository seriesRepository,
            IQualityProfileRankRepository qualityRankRepository)
            : base(connection, eventAggregator)
        {
            _seriesRepository = seriesRepository;
            _qualityRankRepository = qualityRankRepository;
        }

        protected override StdbBlocklist[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.Blocklist.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override Blocklist ToModel(StdbBlocklist row) => new Blocklist
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            EpisodeIds = SpacetimeJson.Deserialize<List<int>>(row.EpisodeIdsJson) ?? new List<int>(),
            SourceTitle = row.SourceTitle,
            Quality = SpacetimeJson.Deserialize<QualityModel>(row.QualityJson),
            Date = SpacetimeDateTime.ToDateTime(row.Date),
            PublishedDate = SpacetimeDateTime.ToDateTime(row.PublishedDate),
            Size = row.Size,
            Protocol = (DownloadProtocol)row.Protocol,
            Indexer = row.Indexer,
            IndexerFlags = (IndexerFlags)row.IndexerFlags,
            ReleaseType = (ReleaseType)row.ReleaseType,
            Message = row.Message,
            Source = row.Source,
            TorrentInfoHash = row.TorrentInfoHash,
            Languages = SpacetimeJson.Deserialize<List<Language>>(row.LanguagesJson) ?? new List<Language>()
        };

        protected override int GetRowId(StdbBlocklist row) => row.Id;

        private static int DeriveQualityId(Blocklist model) => model.Quality?.Quality?.Id ?? 0;

        protected override void InvokeInsertReducer(Blocklist model) => Conn.Connection.Reducers.InsertBlocklist(
            model.SeriesId,
            SpacetimeJson.Serialize(model.EpisodeIds),
            model.SourceTitle ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            SpacetimeDateTime.ToTimestamp(model.Date),
            SpacetimeDateTime.ToTimestamp(model.PublishedDate),
            model.Size,
            (int)model.Protocol,
            model.Indexer ?? string.Empty,
            (int)model.IndexerFlags,
            (int)model.ReleaseType,
            model.Message ?? string.Empty,
            model.Source ?? string.Empty,
            model.TorrentInfoHash ?? string.Empty,
            SpacetimeJson.Serialize(model.Languages));

        protected override void InvokeUpdateReducer(Blocklist model) => Conn.Connection.Reducers.UpdateBlocklist(
            model.Id,
            model.SeriesId,
            SpacetimeJson.Serialize(model.EpisodeIds),
            model.SourceTitle ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            SpacetimeDateTime.ToTimestamp(model.Date),
            SpacetimeDateTime.ToTimestamp(model.PublishedDate),
            model.Size,
            (int)model.Protocol,
            model.Indexer ?? string.Empty,
            (int)model.IndexerFlags,
            (int)model.ReleaseType,
            model.Message ?? string.Empty,
            model.Source ?? string.Empty,
            model.TorrentInfoHash ?? string.Empty,
            SpacetimeJson.Serialize(model.Languages));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteBlocklist(id);

        public List<Blocklist> BlocklistedByTitle(int seriesId, string sourceTitle) =>
            All().Where(b => b.SeriesId == seriesId && b.SourceTitle != null && b.SourceTitle.Contains(sourceTitle)).ToList();

        public List<Blocklist> BlocklistedByTorrentInfoHash(int seriesId, string torrentInfoHash) =>
            All().Where(b => b.SeriesId == seriesId && b.TorrentInfoHash != null && b.TorrentInfoHash.Contains(torrentInfoHash)).ToList();

        public List<Blocklist> BlocklistedBySeries(int seriesId) => All().Where(b => b.SeriesId == seriesId).ToList();

        public void DeleteForSeriesIds(List<int> seriesIds)
        {
            foreach (var row in All().Where(b => seriesIds.Contains(b.SeriesId)).ToList())
            {
                Delete(row.Id);
            }
        }

        // Same fetch-all/refine/paginate approach used throughout Tier 2 - Series is attached
        // client-side (mirrors the real repository's QueryJoined<Blocklist, Series>), and the
        // quality-rank sort is the same keyed (ProfileId, QualityId) lookup as everywhere else.
        public override PagingSpec<Blocklist> GetPaged(PagingSpec<Blocklist> pagingSpec)
        {
            var seriesById = _seriesRepository.All().ToDictionary(s => s.Id);

            var all = All()
                .Select(b =>
                {
                    b.Series = seriesById.GetValueOrDefault(b.SeriesId);
                    return b;
                })
                .ToList();

            pagingSpec.TotalRecords = all.Count;

            if (string.Equals(pagingSpec.SortKey, "quality", StringComparison.OrdinalIgnoreCase))
            {
                var ranks = _qualityRankRepository.All().ToDictionary(r => (r.ProfileId, r.QualityId), r => r.Score);

                double RankFor(Blocklist b) => seriesById.TryGetValue(b.SeriesId, out var series) &&
                    ranks.TryGetValue((series.QualityProfileId, b.Quality?.Quality?.Id ?? 0), out var score) ? score : -1.0;

                var sorted = pagingSpec.SortDirection == SortDirection.Descending
                    ? all.OrderByDescending(RankFor)
                    : all.OrderBy(RankFor);

                pagingSpec.Records = sorted.Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
            }
            else
            {
                var keySelector = SpacetimeSortKey.Resolve<Blocklist>(pagingSpec.SortKey, b => b.Id);

                var sorted = pagingSpec.SortDirection == SortDirection.Descending
                    ? all.OrderByDescending(keySelector)
                    : all.OrderBy(keySelector);

                pagingSpec.Records = sorted.Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
            }

            return pagingSpec;
        }
    }
}
