using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.History;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbEpisodeHistory = SpacetimeDB.Types.EpisodeHistory;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// QualityId mirrors SpacetimeMediaFileRepository's EpisodeFile precedent exactly - derived
    /// from the submitted Quality document at exactly one choke point
    /// (InvokeInsertReducer/InvokeUpdateReducer), never accepted independently from a caller.
    /// </summary>
    public class SpacetimeHistoryRepository : SpacetimeBasicRepository<EpisodeHistory, StdbEpisodeHistory>, IHistoryRepository
    {
        private readonly ISeriesRepository _seriesRepository;
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IQualityProfileRankRepository _qualityRankRepository;

        public SpacetimeHistoryRepository(
            ISpacetimeDbConnection connection,
            IEventAggregator eventAggregator,
            ISeriesRepository seriesRepository,
            IEpisodeRepository episodeRepository,
            IQualityProfileRankRepository qualityRankRepository)
            : base(connection, eventAggregator)
        {
            _seriesRepository = seriesRepository;
            _episodeRepository = episodeRepository;
            _qualityRankRepository = qualityRankRepository;
        }

        protected override RemoteTableHandle<EventContext, StdbEpisodeHistory> Table => Conn.Connection.Db.EpisodeHistory;

        protected override StdbEpisodeHistory FindRowById(int id) => Conn.Connection.Db.EpisodeHistory.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int p3, string p4, string p5, int p6, SpacetimeDB.Timestamp p7, int p8, string p9, string p10, string p11)
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

            Conn.Connection.Reducers.OnUpdateEpisodeHistory += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateEpisodeHistory -= Handler);
        }

        protected override EpisodeHistory ToModel(StdbEpisodeHistory row) => new EpisodeHistory
        {
            Id = row.Id,
            EpisodeId = row.EpisodeId,
            SeriesId = row.SeriesId,
            SourceTitle = row.SourceTitle,
            Quality = SpacetimeJson.Deserialize<QualityModel>(row.QualityJson),
            Date = SpacetimeDateTime.ToDateTime(row.Date),
            EventType = (EpisodeHistoryEventType)row.EventType,
            Data = SpacetimeJson.Deserialize<Dictionary<string, string>>(row.DataJson) ?? new Dictionary<string, string>(),
            Languages = SpacetimeJson.Deserialize<List<Language>>(row.LanguagesJson) ?? new List<Language>(),
            DownloadId = row.DownloadId
        };

        protected override int GetRowId(StdbEpisodeHistory row) => row.Id;

        private static int DeriveQualityId(EpisodeHistory model) => model.Quality?.Quality?.Id ?? 0;

        public override void MigrateInsert(EpisodeHistory model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertEpisodeHistory(
            model.Id,
            model.EpisodeId,
            model.SeriesId,
            model.SourceTitle ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            SpacetimeDateTime.ToTimestamp(model.Date),
            (int)model.EventType,
            SpacetimeJson.Serialize(model.Data),
            SpacetimeJson.Serialize(model.Languages),
            model.DownloadId ?? string.Empty));

        protected override void InvokeInsertReducer(EpisodeHistory model) => Conn.Connection.Reducers.InsertEpisodeHistory(
            model.EpisodeId,
            model.SeriesId,
            model.SourceTitle ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            SpacetimeDateTime.ToTimestamp(model.Date),
            (int)model.EventType,
            SpacetimeJson.Serialize(model.Data),
            SpacetimeJson.Serialize(model.Languages),
            model.DownloadId ?? string.Empty);

        protected override void InvokeUpdateReducer(EpisodeHistory model) => Conn.Connection.Reducers.UpdateEpisodeHistory(
            model.Id,
            model.EpisodeId,
            model.SeriesId,
            model.SourceTitle ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            SpacetimeDateTime.ToTimestamp(model.Date),
            (int)model.EventType,
            SpacetimeJson.Serialize(model.Data),
            SpacetimeJson.Serialize(model.Languages),
            model.DownloadId ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteEpisodeHistory(id);

        public EpisodeHistory MostRecentForEpisode(int episodeId) =>
            All().Where(h => h.EpisodeId == episodeId).OrderByDescending(h => h.Date).FirstOrDefault();

        public List<EpisodeHistory> FindByEpisodeId(int episodeId) =>
            All().Where(h => h.EpisodeId == episodeId).OrderByDescending(h => h.Date).ToList();

        public EpisodeHistory MostRecentForDownloadId(string downloadId) =>
            All().Where(h => h.DownloadId == downloadId).OrderByDescending(h => h.Date).FirstOrDefault();

        public List<EpisodeHistory> FindByDownloadId(string downloadId) =>
            All().Where(h => h.DownloadId == downloadId).ToList();

        public List<EpisodeHistory> GetBySeries(int seriesId, EpisodeHistoryEventType? eventType) =>
            All().Where(h => h.SeriesId == seriesId && (!eventType.HasValue || h.EventType == eventType.Value))
                 .OrderByDescending(h => h.Date).ToList();

        public List<EpisodeHistory> GetBySeason(int seriesId, int seasonNumber, EpisodeHistoryEventType? eventType)
        {
            var episodesById = _episodeRepository.GetEpisodes(seriesId).Where(e => e.SeasonNumber == seasonNumber).ToDictionary(e => e.Id);

            return All()
                .Where(h => h.SeriesId == seriesId && episodesById.ContainsKey(h.EpisodeId) && (!eventType.HasValue || h.EventType == eventType.Value))
                .Select(h =>
                {
                    h.Episode = episodesById[h.EpisodeId];
                    return h;
                })
                .OrderByDescending(h => h.Date).ToList();
        }

        public List<EpisodeHistory> GetByEpisode(int episodeId, EpisodeHistoryEventType? eventType) =>
            All().Where(h => h.EpisodeId == episodeId && (!eventType.HasValue || h.EventType == eventType.Value))
                 .OrderByDescending(h => h.Date).ToList();

        public List<EpisodeHistory> FindDownloadHistory(int idSeriesId, QualityModel quality) =>
            All().Where(h => h.SeriesId == idSeriesId &&
                              h.Quality != null && quality != null && h.Quality.Equals(quality) &&
                              (h.EventType == EpisodeHistoryEventType.Grabbed ||
                               h.EventType == EpisodeHistoryEventType.DownloadFailed ||
                               h.EventType == EpisodeHistoryEventType.DownloadFolderImported))
                 .ToList();

        public void DeleteForSeries(List<int> seriesIds)
        {
            foreach (var row in All().Where(h => seriesIds.Contains(h.SeriesId)).ToList())
            {
                Delete(row.Id);
            }
        }

        public List<EpisodeHistory> Since(DateTime date, EpisodeHistoryEventType? eventType)
        {
            var seriesById = _seriesRepository.All().ToDictionary(s => s.Id);
            var episodesById = _episodeRepository.All().ToDictionary(e => e.Id);

            // Series is required, not optional - HistoryResourceMapper.ToResource reads
            // model.Series.QualityProfile.Value unconditionally (not gated by includeSeries).
            // The real repository's Series join is an inner join (Join<EpisodeHistory, Series>,
            // not LeftJoin), so a row whose series has since been deleted is excluded from
            // results entirely rather than mapped with a null Series - match that here too.
            return All()
                .Where(h => h.Date >= date && (!eventType.HasValue || h.EventType == eventType.Value))
                .Where(h => seriesById.ContainsKey(h.SeriesId))
                .Select(h =>
                {
                    h.Series = seriesById[h.SeriesId];
                    h.Episode = episodesById.GetValueOrDefault(h.EpisodeId);
                    return h;
                })
                .OrderBy(h => h.Date).ToList();
        }

        // Same fetch-all/refine-in-C#/paginate-the-refined-set approach as Episode's paginated
        // queries - LIKE-on-serialized-JSON language filtering and the quality-rank sort both
        // become client-side operations, since neither has a SpacetimeDB WHERE equivalent.
        public PagingSpec<EpisodeHistory> GetPaged(PagingSpec<EpisodeHistory> pagingSpec, int[] languages, int[] qualities)
        {
            var seriesById = _seriesRepository.All().ToDictionary(s => s.Id);
            var episodesById = _episodeRepository.All().ToDictionary(e => e.Id);

            // Series is required (see Since's comment above) - excluded here the same way,
            // matching the real repository's inner join instead of leaving a null Series behind.
            var filtered = All()
                .Where(h => languages == null || languages.Length == 0 || (h.Languages != null && h.Languages.Any(l => languages.Contains(l.Id))))
                .Where(h => qualities == null || qualities.Length == 0 || qualities.Contains(h.Quality?.Quality?.Id ?? -1))
                .Where(h => seriesById.ContainsKey(h.SeriesId))
                .Select(h =>
                {
                    h.Series = seriesById[h.SeriesId];
                    h.Episode = episodesById.GetValueOrDefault(h.EpisodeId);
                    return h;
                })
                .ToList();

            // HistoryController compiles eventTypes/episodeId/downloadId/seriesIds query-param
            // filters into pagingSpec.FilterExpressions and relies on GetPaged to apply them - the
            // base class's default GetPaged does this, but this override replaces that base
            // implementation entirely, so it has to reapply the same step itself.
            foreach (var filter in pagingSpec.FilterExpressions)
            {
                filtered = filtered.Where(filter.Compile()).ToList();
            }

            pagingSpec.TotalRecords = filtered.Count;

            if (string.Equals(pagingSpec.SortKey, "quality", StringComparison.OrdinalIgnoreCase))
            {
                var ranks = _qualityRankRepository.All().ToDictionary(r => (r.ProfileId, r.QualityId), r => r.Score);

                double RankFor(EpisodeHistory h) => seriesById.TryGetValue(h.SeriesId, out var series) &&
                    ranks.TryGetValue((series.QualityProfileId, h.Quality?.Quality?.Id ?? 0), out var score) ? score : -1.0;

                var sorted = pagingSpec.SortDirection == SortDirection.Descending
                    ? filtered.OrderByDescending(RankFor)
                    : filtered.OrderBy(RankFor);

                pagingSpec.Records = sorted.Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
            }
            else
            {
                var keySelector = SpacetimeSortKey.Resolve<EpisodeHistory>(pagingSpec.SortKey, h => h.Id);

                var sorted = pagingSpec.SortDirection == SortDirection.Descending
                    ? filtered.OrderByDescending(keySelector)
                    : filtered.OrderBy(keySelector);

                pagingSpec.Records = sorted.Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
            }

            return pagingSpec;
        }
    }
}
