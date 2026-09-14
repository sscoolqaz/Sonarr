using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbEpisode = SpacetimeDB.Types.Episode;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Series/EpisodeFile are joined client-side via the already-registered SpacetimeSeriesDb
    /// repositories rather than a SQL join - SpacetimeDB subscriptions/RemoteQuery don't support
    /// arbitrary multi-table joins, and reusing IMediaFileRepository/ISeriesRepository avoids
    /// duplicating their JSON deserialization logic here.
    /// </summary>
    public class SpacetimeEpisodeRepository : SpacetimeBasicRepository<Episode, StdbEpisode>, IEpisodeRepository
    {
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IQualityProfileRankRepository _qualityRankRepository;

        public SpacetimeEpisodeRepository(
            ISpacetimeDbConnection connection,
            IEventAggregator eventAggregator,
            IMediaFileRepository mediaFileRepository,
            ISeriesRepository seriesRepository,
            IQualityProfileRankRepository qualityRankRepository)
            : base(connection, eventAggregator)
        {
            _mediaFileRepository = mediaFileRepository;
            _seriesRepository = seriesRepository;
            _qualityRankRepository = qualityRankRepository;
        }

        protected override RemoteTableHandle<EventContext, StdbEpisode> Table => Conn.Connection.Db.Episode;

        protected override StdbEpisode FindRowById(int id) => Conn.Connection.Db.Episode.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int p3, int p4, int p5, int p6, string p7, string p8, SpacetimeDB.Timestamp? p9, string p10, bool p11, int? p12, int? p13, int? p14, int? p15, int? p16, int? p17, int? p18, bool p19, string p20, string p21, SpacetimeDB.Timestamp? p22, int p23, string p24)
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

            Conn.Connection.Reducers.OnUpdateEpisode += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateEpisode -= Handler);
        }

        // SeriesTitle/Series/AbsoluteEpisodeNumberAdded are Ignore()'d in the real TableMapping -
        // no columns for them here either. EpisodeFile is LazyLoaded there too, but unlike
        // RootFolderPath-style Ignore()'d fields it's not safe to leave unpopulated:
        // EpisodeControllerWithSignalR.MapToResource reads episode.EpisodeFile.Value directly
        // whenever includeEpisodeFile && EpisodeFileId != 0 (the default for most episode list/
        // detail requests), and a bare null LazyLoaded<T> NREs on .Value access - the same failure
        // mode QualityProfile had on SpacetimeSeriesRepository (see that file's comment).
        protected override Episode ToModel(StdbEpisode row) => new Episode
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            TvdbId = row.TvdbId,
            EpisodeFileId = row.EpisodeFileId,
            EpisodeFile = row.EpisodeFileId != 0 ? new LazyLoaded<EpisodeFile>(_mediaFileRepository.Find(row.EpisodeFileId)) : null,
            SeasonNumber = row.SeasonNumber,
            EpisodeNumber = row.EpisodeNumber,
            Title = row.Title,
            AirDate = row.AirDate,
            AirDateUtc = SpacetimeDateTime.ToDateTime(row.AirDateUtc),
            Overview = row.Overview,
            Monitored = row.Monitored,
            AbsoluteEpisodeNumber = row.AbsoluteEpisodeNumber,
            SceneAbsoluteEpisodeNumber = row.SceneAbsoluteEpisodeNumber,
            SceneSeasonNumber = row.SceneSeasonNumber,
            SceneEpisodeNumber = row.SceneEpisodeNumber,
            AiredAfterSeasonNumber = row.AiredAfterSeasonNumber,
            AiredBeforeSeasonNumber = row.AiredBeforeSeasonNumber,
            AiredBeforeEpisodeNumber = row.AiredBeforeEpisodeNumber,
            UnverifiedSceneNumbering = row.UnverifiedSceneNumbering,
            Ratings = SpacetimeJson.Deserialize<Ratings>(row.RatingsJson),
            Images = SpacetimeJson.Deserialize<List<MediaCover.MediaCover>>(row.ImagesJson) ?? new List<MediaCover.MediaCover>(),
            LastSearchTime = SpacetimeDateTime.ToDateTime(row.LastSearchTime),
            Runtime = row.Runtime,
            FinaleType = row.FinaleType
        };

        protected override int GetRowId(StdbEpisode row) => row.Id;

        public override void MigrateInsert(Episode model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertEpisode(
            model.Id,
            model.SeriesId,
            model.TvdbId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.EpisodeNumber,
            model.Title ?? string.Empty,
            model.AirDate ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.AirDateUtc),
            model.Overview ?? string.Empty,
            model.Monitored,
            model.AbsoluteEpisodeNumber,
            model.SceneAbsoluteEpisodeNumber,
            model.SceneSeasonNumber,
            model.SceneEpisodeNumber,
            model.AiredAfterSeasonNumber,
            model.AiredBeforeSeasonNumber,
            model.AiredBeforeEpisodeNumber,
            model.UnverifiedSceneNumbering,
            SpacetimeJson.Serialize(model.Ratings),
            SpacetimeJson.Serialize(model.Images),
            SpacetimeDateTime.ToTimestamp(model.LastSearchTime),
            model.Runtime,
            model.FinaleType ?? string.Empty));

        protected override void InvokeInsertReducer(Episode model) => Conn.Connection.Reducers.InsertEpisode(
            model.SeriesId,
            model.TvdbId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.EpisodeNumber,
            model.Title ?? string.Empty,
            model.AirDate ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.AirDateUtc),
            model.Overview ?? string.Empty,
            model.Monitored,
            model.AbsoluteEpisodeNumber,
            model.SceneAbsoluteEpisodeNumber,
            model.SceneSeasonNumber,
            model.SceneEpisodeNumber,
            model.AiredAfterSeasonNumber,
            model.AiredBeforeSeasonNumber,
            model.AiredBeforeEpisodeNumber,
            model.UnverifiedSceneNumbering,
            SpacetimeJson.Serialize(model.Ratings),
            SpacetimeJson.Serialize(model.Images),
            SpacetimeDateTime.ToTimestamp(model.LastSearchTime),
            model.Runtime,
            model.FinaleType ?? string.Empty);

        protected override void InvokeUpdateReducer(Episode model) => Conn.Connection.Reducers.UpdateEpisode(
            model.Id,
            model.SeriesId,
            model.TvdbId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.EpisodeNumber,
            model.Title ?? string.Empty,
            model.AirDate ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.AirDateUtc),
            model.Overview ?? string.Empty,
            model.Monitored,
            model.AbsoluteEpisodeNumber,
            model.SceneAbsoluteEpisodeNumber,
            model.SceneSeasonNumber,
            model.SceneEpisodeNumber,
            model.AiredAfterSeasonNumber,
            model.AiredBeforeSeasonNumber,
            model.AiredBeforeEpisodeNumber,
            model.UnverifiedSceneNumbering,
            SpacetimeJson.Serialize(model.Ratings),
            SpacetimeJson.Serialize(model.Images),
            SpacetimeDateTime.ToTimestamp(model.LastSearchTime),
            model.Runtime,
            model.FinaleType ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteEpisode(id);

        public Episode Find(int seriesId, int season, int episodeNumber) =>
            All().SingleOrDefault(e => e.SeriesId == seriesId && e.SeasonNumber == season && e.EpisodeNumber == episodeNumber);

        public Episode Find(int seriesId, int absoluteEpisodeNumber) =>
            All().SingleOrDefault(e => e.SeriesId == seriesId && e.AbsoluteEpisodeNumber == absoluteEpisodeNumber);

        public List<Episode> Find(int seriesId, string date) =>
            All().Where(e => e.SeriesId == seriesId && e.AirDate == date).ToList();

        public List<Episode> GetEpisodes(int seriesId) => All().Where(e => e.SeriesId == seriesId).ToList();

        public List<Episode> GetEpisodes(int seriesId, int seasonNumber) =>
            All().Where(e => e.SeriesId == seriesId && e.SeasonNumber == seasonNumber).ToList();

        public List<Episode> GetEpisodesBySeriesIds(List<int> seriesIds) =>
            All().Where(e => seriesIds.Contains(e.SeriesId)).ToList();

        public List<Episode> GetEpisodesBySceneSeason(int seriesId, int sceneSeasonNumber) =>
            All().Where(e => e.SeriesId == seriesId && e.SceneSeasonNumber == sceneSeasonNumber).ToList();

        public List<Episode> GetEpisodeByFileId(int fileId) => All().Where(e => e.EpisodeFileId == fileId).ToList();

        public List<Episode> EpisodesWithFiles(int seriesId)
        {
            var files = _mediaFileRepository.GetFilesBySeries(seriesId).ToDictionary(f => f.Id);

            return All()
                .Where(e => e.SeriesId == seriesId && files.ContainsKey(e.EpisodeFileId))
                .Select(e =>
                {
                    e.EpisodeFile = files[e.EpisodeFileId];
                    return e;
                })
                .ToList();
        }

        // Phase 3 schema design decision for this exact query: the real predicate is
        // AirDateUtc + Series.Runtime <= now, real date arithmetic SpacetimeDB's WHERE can't
        // express, and applying it after a coarse paginated fetch would produce a short page and
        // a wrong TotalRecords (the coarse set and the true page aren't the same rows). Fetch the
        // entire coarse candidate set, refine every predicate in C#, then paginate the fully
        // refined in-memory list - an honest scale tradeoff, acceptable at Sonarr's realistic
        // library size, not a free translation.
        public PagingSpec<Episode> EpisodesWithoutFiles(PagingSpec<Episode> pagingSpec, bool includeSpecials, HashSet<int> seriesTags = null)
        {
            var now = DateTime.UtcNow;
            var startingSeasonNumber = includeSpecials ? 0 : 1;
            var seriesById = _seriesRepository.All().ToDictionary(s => s.Id);

            var refined = All()
                .Where(e => e.EpisodeFileId == 0 && e.SeasonNumber >= startingSeasonNumber)
                .Where(e => seriesById.ContainsKey(e.SeriesId))
                .Select(e =>
                {
                    e.Series = seriesById[e.SeriesId];
                    return e;
                })
                .Where(e => e.AirDateUtc.HasValue
                    && e.AirDateUtc.Value.AddMinutes(e.Series.Runtime) <= now
                    && (seriesTags == null || seriesTags.Count == 0 || seriesTags.Overlaps(e.Series.Tags ?? new HashSet<int>())))
                .ToList();

            // MissingController/CutoffController add a Monitored/Series.Monitored filter onto
            // pagingSpec.FilterExpressions and rely on GetPaged to apply it - the base class's
            // default GetPaged does this, but this method (like EpisodesWhereCutoffUnmet below)
            // replaces that base implementation entirely, so it has to reapply the same step
            // itself. Series must be attached first (above) since that filter reads v.Series.
            foreach (var filter in pagingSpec.FilterExpressions)
            {
                refined = refined.Where(filter.Compile()).ToList();
            }

            pagingSpec.TotalRecords = refined.Count;
            pagingSpec.Records = Paginate(refined, pagingSpec);

            return pagingSpec;
        }

        // Same fetch-all-then-refine-then-paginate approach as EpisodesWithoutFiles above.
        // qualitiesBelowCutoff is the caller-precomputed (ProfileId, QualityIds) set that counts
        // as "below cutoff" for each profile - the keyed lookup this repository performs is a
        // plain set-membership test against that, not a MAX/aggregate (see the Phase 3 design
        // review's correction for why MAX was wrong here in the first place).
        public PagingSpec<Episode> EpisodesWhereCutoffUnmet(PagingSpec<Episode> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, bool includeSpecials, HashSet<int> seriesTags = null, List<int> quality = null)
        {
            var startingSeasonNumber = includeSpecials ? 0 : 1;
            var seriesById = _seriesRepository.All().ToDictionary(s => s.Id);
            var filesById = _mediaFileRepository.All().ToDictionary(f => f.Id);
            var belowCutoffByProfile = qualitiesBelowCutoff.ToDictionary(q => q.ProfileId, q => new HashSet<int>(q.QualityIds));

            var refined = new List<Episode>();

            foreach (var episode in All().Where(e => e.EpisodeFileId != 0 && e.SeasonNumber >= startingSeasonNumber))
            {
                if (!seriesById.TryGetValue(episode.SeriesId, out var series) || !filesById.TryGetValue(episode.EpisodeFileId, out var file))
                {
                    continue;
                }

                var qualityId = file.Quality?.Quality?.Id ?? 0;

                if (!belowCutoffByProfile.TryGetValue(series.QualityProfileId, out var belowCutoffIds) || !belowCutoffIds.Contains(qualityId))
                {
                    continue;
                }

                if (seriesTags is { Count: > 0 } && !seriesTags.Overlaps(series.Tags ?? new HashSet<int>()))
                {
                    continue;
                }

                if (quality is { Count: > 0 } && !quality.Contains(qualityId))
                {
                    continue;
                }

                episode.Series = series;
                refined.Add(episode);
            }

            // See EpisodesWithoutFiles above - same reapplication of pagingSpec.FilterExpressions
            // needed here since this method also replaces the base class's default GetPaged.
            foreach (var filter in pagingSpec.FilterExpressions)
            {
                refined = refined.Where(filter.Compile()).ToList();
            }

            pagingSpec.TotalRecords = refined.Count;

            if (string.Equals(pagingSpec.SortKey, "quality", StringComparison.OrdinalIgnoreCase))
            {
                var ranks = _qualityRankRepository.All().ToDictionary(r => (r.ProfileId, r.QualityId), r => r.Score);

                double RankFor(Episode e) => filesById.TryGetValue(e.EpisodeFileId, out var f) &&
                    ranks.TryGetValue((seriesById[e.SeriesId].QualityProfileId, f.Quality?.Quality?.Id ?? 0), out var score) ? score : -1.0;

                var sorted = pagingSpec.SortDirection == SortDirection.Descending
                    ? refined.OrderByDescending(RankFor)
                    : refined.OrderBy(RankFor);

                pagingSpec.Records = sorted.Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
            }
            else
            {
                pagingSpec.Records = Paginate(refined, pagingSpec);
            }

            return pagingSpec;
        }

        public List<Episode> FindEpisodesBySceneNumbering(int seriesId, int seasonNumber, int episodeNumber) =>
            All().Where(e => e.SeriesId == seriesId && e.SceneSeasonNumber == seasonNumber && e.SceneEpisodeNumber == episodeNumber).ToList();

        public List<Episode> FindEpisodesBySceneNumbering(int seriesId, int sceneAbsoluteEpisodeNumber) =>
            All().Where(e => e.SeriesId == seriesId && e.SceneAbsoluteEpisodeNumber == sceneAbsoluteEpisodeNumber).ToList();

        public List<Episode> EpisodesBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored, bool includeSpecials)
        {
            var seriesById = includeUnmonitored ? null : _seriesRepository.All().ToDictionary(s => s.Id);

            return All()
                .Where(e => e.AirDateUtc >= startDate && e.AirDateUtc <= endDate)
                .Where(e => includeSpecials || e.SeasonNumber != 0)
                .Where(e => includeUnmonitored || (e.Monitored && seriesById.TryGetValue(e.SeriesId, out var series) && series.Monitored))
                .ToList();
        }

        public void SetMonitoredFlat(Episode episode, bool monitored)
        {
            episode.Monitored = monitored;
            SetFields(episode, e => e.Monitored);
            ModelUpdated(episode, true);
        }

        public void SetMonitoredBySeason(int seriesId, int seasonNumber, bool monitored)
        {
            foreach (var episode in All().Where(e => e.SeriesId == seriesId && e.SeasonNumber == seasonNumber && e.Monitored != monitored).ToList())
            {
                episode.Monitored = monitored;
                SetFields(episode, e => e.Monitored);
            }
        }

        public void SetMonitored(IEnumerable<int> ids, bool monitored)
        {
            foreach (var id in ids)
            {
                SetFields(new Episode { Id = id, Monitored = monitored }, e => e.Monitored);
            }
        }

        public List<int> SetMonitored(int seriesId, MonitorTypes monitor, int firstSeason, int lastSeason)
        {
            if (monitor is MonitorTypes.MonitorSpecials or MonitorTypes.UnmonitorSpecials)
            {
                var shouldMonitor = monitor == MonitorTypes.MonitorSpecials;

                foreach (var episode in All().Where(e => e.SeriesId == seriesId && e.SeasonNumber == 0 && e.Monitored != shouldMonitor).ToList())
                {
                    episode.Monitored = shouldMonitor;
                    SetFields(episode, e => e.Monitored);
                }

                return GetSeasonNumbersWithMonitoredEpisodes(seriesId);
            }

            if (monitor == MonitorTypes.None)
            {
                foreach (var episode in All().Where(e => e.SeriesId == seriesId && e.Monitored).ToList())
                {
                    episode.Monitored = false;
                    SetFields(episode, e => e.Monitored);
                }

                return new List<int>();
            }

            var now = DateTime.UtcNow;

            bool? Predicate(Episode e)
            {
                if (e.SeasonNumber <= 0)
                {
                    return false;
                }

#pragma warning disable CS0612
                return monitor switch
                {
                    MonitorTypes.All => true,
                    MonitorTypes.Future => e.AirDateUtc == null || e.AirDateUtc >= now,
                    MonitorTypes.Missing => e.EpisodeFileId == 0,
                    MonitorTypes.Existing => e.EpisodeFileId != 0,
                    MonitorTypes.Pilot => e.SeasonNumber == firstSeason && e.EpisodeNumber == 1,
                    MonitorTypes.FirstSeason => e.SeasonNumber == firstSeason,
                    MonitorTypes.LastSeason or MonitorTypes.LatestSeason => e.SeasonNumber == lastSeason,
                    MonitorTypes.Recent => e.AirDateUtc == null || e.AirDateUtc >= now.AddDays(-90),
                    _ => (bool?)null
                };
#pragma warning restore CS0612
            }

            foreach (var episode in All().Where(e => e.SeriesId == seriesId).ToList())
            {
                var shouldMonitor = Predicate(episode);

                if (shouldMonitor.HasValue && episode.Monitored != shouldMonitor.Value)
                {
                    episode.Monitored = shouldMonitor.Value;
                    SetFields(episode, e => e.Monitored);
                }
            }

            return GetSeasonNumbersWithMonitoredEpisodes(seriesId);
        }

        public void SetFileId(Episode episode, int fileId)
        {
            episode.EpisodeFileId = fileId;
            SetFields(episode, e => e.EpisodeFileId);
            ModelUpdated(episode, true);
        }

        public void ClearFileId(Episode episode, bool unmonitor)
        {
            episode.EpisodeFileId = 0;
            episode.Monitored &= !unmonitor;
            SetFields(episode, e => e.EpisodeFileId, e => e.Monitored);
            ModelUpdated(episode, true);
        }

        private List<int> GetSeasonNumbersWithMonitoredEpisodes(int seriesId) =>
            All().Where(e => e.SeriesId == seriesId && e.Monitored).Select(e => e.SeasonNumber).Distinct().ToList();

        private static List<Episode> Paginate(List<Episode> source, PagingSpec<Episode> pagingSpec)
        {
            var keySelector = SpacetimeSortKey.Resolve<Episode>(pagingSpec.SortKey, e => e.Id);

            var sorted = pagingSpec.SortDirection == SortDirection.Descending
                ? source.OrderByDescending(keySelector)
                : source.OrderBy(keySelector);

            return sorted.Skip(Math.Max(pagingSpec.Page - 1, 0) * pagingSpec.PageSize).Take(pagingSpec.PageSize).ToList();
        }
    }
}
