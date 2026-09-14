using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tv;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbSeries = SpacetimeDB.Types.Series;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeSeriesRepository : SpacetimeBasicRepository<Series, StdbSeries>, ISeriesRepository
    {
        public SpacetimeSeriesRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbSeries> Table => Conn.Connection.Db.Series;

        protected override StdbSeries FindRowById(int id) => Conn.Connection.Db.Series.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int p3, int p4, string p5, int p6, string p7, string p8, string p9, string p10, string p11, int p12, string p13, string p14, bool p15, int p16, int p17, bool p18, SpacetimeDB.Timestamp? p19, int p20, string p21, int p22, string p23, bool p24, string p25, string p26, int p27, string p28, string p29, string p30, string p31, SpacetimeDB.Timestamp p32, SpacetimeDB.Timestamp? p33, SpacetimeDB.Timestamp? p34, string p35, string p36, string p37, string p38, System.Collections.Generic.List<int> p39)
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

            Conn.Connection.Reducers.OnUpdateSeries += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateSeries -= Handler);
        }

        protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id)
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

            Conn.Connection.Reducers.OnDeleteSeries += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteSeries -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int p1, int p2, int p3, string p4, int p5, string p6, string p7, string p8, string p9, string p10, int p11, string p12, string p13, bool p14, int p15, int p16, bool p17, SpacetimeDB.Timestamp? p18, int p19, string p20, int p21, string p22, bool p23, string p24, string p25, int p26, string p27, string p28, string p29, string p30, SpacetimeDB.Timestamp p31, SpacetimeDB.Timestamp? p32, SpacetimeDB.Timestamp? p33, string p34, string p35, string p36, string p37, System.Collections.Generic.List<int> p38)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted();
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnInsertSeries += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertSeries -= Handler);
        }

        // TODO: this is a full table scan per series (Conn.Connection.Db.SeriesTag.Iter().Where)
        // - switch to an index-based lookup once a secondary index on SeriesTag.SeriesId is
        // available in this SpacetimeDB.Runtime version (tracked alongside the module-side
        // schema work).
        private List<int> TagIdsFor(int seriesId) =>
            Conn.RunOnActor(() => Conn.Connection.Db.SeriesTag.Iter().Where(t => t.SeriesId == seriesId).Select(t => t.TagId).ToList());

        // QualityProfile is LazyLoaded (only QualityProfileId is a real column). The real
        // TableMapping's .HasOne(s => s.QualityProfile, ...) join means every Series it returns
        // already carries a populated QualityProfile - HistoryResourceMapper relies on this,
        // reading model.Series.QualityProfile.Value directly instead of re-fetching by id. There's
        // no Spacetime join to mirror that with, so it's fetched by hand here instead (same
        // eager-attach approach as TagIdsFor above); a bare null LazyLoaded<T> field NREs on
        // .Value access, so this can't be left unpopulated the way RootFolderPath below can.
        // RootFolderPath itself is Ignore()'d in the real TableMapping (computed post-load
        // elsewhere), so it's left at its model default - there is no column to read it from.
        //
        // ToModel is an unchanged sync hook, always invoked from inside an already-actor-thread
        // context (see SpacetimeBasicRepository.Query/All), so reading
        // Conn.Connection.Db.QualityProfile directly here - the same shared RemoteTables/Db every
        // repository already reaches via Conn.Connection.Db - is safe: this is an explicit direct
        // table read on the actor thread, not a re-entrant call into another repository's own
        // public async API (which used to be exactly what happened here, via
        // IQualityProfileRepository.Find(...).GetAwaiter().GetResult() - only ever safe because
        // Conn.RunOnActorAsync detects the reentrancy and runs inline instead of queuing/blocking,
        // an implicit dependency this rewrite removes).
        protected override Series ToModel(StdbSeries row) => new Series
        {
            Id = row.Id,
            TvdbId = row.TvdbId,
            TvRageId = row.TvRageId,
            TvMazeId = row.TvMazeId,
            ImdbId = row.ImdbId,
            TmdbId = row.TmdbId,
            MalIds = SpacetimeJson.Deserialize<HashSet<int>>(row.MalIdsJson) ?? new HashSet<int>(),
            AniListIds = SpacetimeJson.Deserialize<HashSet<int>>(row.AniListIdsJson) ?? new HashSet<int>(),
            Title = row.Title,
            CleanTitle = row.CleanTitle,
            SortTitle = row.SortTitle,
            Status = (SeriesStatusType)row.Status,
            Overview = row.Overview,
            AirTime = row.AirTime,
            Monitored = row.Monitored,
            MonitorNewItems = (NewItemMonitorTypes)row.MonitorNewItems,
            QualityProfileId = row.QualityProfileId,
            QualityProfile = MapQualityProfile(row.QualityProfileId),
            SeasonFolder = row.SeasonFolder,
            LastInfoSync = SpacetimeDateTime.ToDateTime(row.LastInfoSync),
            Runtime = row.Runtime,
            Images = SpacetimeJson.Deserialize<List<MediaCover.MediaCover>>(row.ImagesJson) ?? new List<MediaCover.MediaCover>(),
            SeriesType = (SeriesTypes)row.SeriesType,
            Network = row.Network,
            UseSceneNumbering = row.UseSceneNumbering,
            TitleSlug = row.TitleSlug,
            Path = row.Path,
            Year = row.Year,
            Ratings = SpacetimeJson.Deserialize<Ratings>(row.RatingsJson),
            Genres = SpacetimeJson.Deserialize<List<string>>(row.GenresJson) ?? new List<string>(),
            Actors = SpacetimeJson.Deserialize<List<Actor>>(row.ActorsJson) ?? new List<Actor>(),
            Certification = row.Certification,
            Added = SpacetimeDateTime.ToDateTime(row.Added),
            FirstAired = SpacetimeDateTime.ToDateTime(row.FirstAired),
            LastAired = SpacetimeDateTime.ToDateTime(row.LastAired),
            OriginalLanguage = SpacetimeJson.Deserialize<Language>(row.OriginalLanguageJson) ?? Language.English,
            OriginalCountry = row.OriginalCountry,
            Seasons = SpacetimeJson.Deserialize<List<Season>>(row.SeasonsJson) ?? new List<Season>(),
            AddOptions = SpacetimeJson.Deserialize<AddSeriesOptions>(row.AddOptionsJson),
            Tags = new HashSet<int>(TagIdsFor(row.Id))
        };

        // Direct table read (Conn.Connection.Db.QualityProfile), not a call into
        // IQualityProfileRepository - see the big comment above ToModel for why. Only ever
        // called from within ToModel, itself always invoked from inside an already-actor-thread
        // RunOnActorAsync closure.
        private LazyLoaded<QualityProfile> MapQualityProfile(int qualityProfileId)
        {
            var row = Conn.Connection.Db.QualityProfile.Id.Find(qualityProfileId);
            return new LazyLoaded<QualityProfile>(row == null ? null : SpacetimeQualityProfileRepository.MapRow(row, Conn));
        }

        protected override int GetRowId(StdbSeries row) => row.Id;

        // Tags aren't part of this - migration calls replace_series_tags separately afterward,
        // same as the running app does (see ReplaceSeriesTags's own reducer, already keyed by
        // seriesId/tagIds rather than needing an id-preserving variant of its own).
        public override Task MigrateInsert(Series model)
        {
            return InvokeAndWaitForMigrateInsert(model.Id, () =>
            {
            Conn.Connection.Reducers.MigrateInsertSeries(
                model.Id,
                model.TvdbId,
                model.TvRageId,
                model.TvMazeId,
                model.ImdbId ?? string.Empty,
                model.TmdbId,
                SpacetimeJson.Serialize(model.MalIds),
                SpacetimeJson.Serialize(model.AniListIds),
                model.Title ?? string.Empty,
                model.CleanTitle ?? string.Empty,
                model.SortTitle ?? string.Empty,
                (int)model.Status,
                model.Overview ?? string.Empty,
                model.AirTime ?? string.Empty,
                model.Monitored,
                (int)model.MonitorNewItems,
                model.QualityProfileId,
                model.SeasonFolder,
                SpacetimeDateTime.ToTimestamp(model.LastInfoSync),
                model.Runtime,
                SpacetimeJson.Serialize(model.Images),
                (int)model.SeriesType,
                model.Network ?? string.Empty,
                model.UseSceneNumbering,
                model.TitleSlug ?? string.Empty,
                model.Path ?? string.Empty,
                model.Year,
                SpacetimeJson.Serialize(model.Ratings),
                SpacetimeJson.Serialize(model.Genres),
                SpacetimeJson.Serialize(model.Actors),
                model.Certification ?? string.Empty,
                SpacetimeDateTime.ToTimestamp(model.Added),
                SpacetimeDateTime.ToTimestamp(model.FirstAired),
                SpacetimeDateTime.ToTimestamp(model.LastAired),
                SpacetimeJson.Serialize(model.OriginalLanguage),
                model.OriginalCountry ?? string.Empty,
                SpacetimeJson.Serialize(model.Seasons),
                SpacetimeJson.Serialize(model.AddOptions));
        });
        }

        protected override void InvokeInsertReducer(Series model)
        {
            Conn.Connection.Reducers.InsertSeries(
                model.TvdbId,
                model.TvRageId,
                model.TvMazeId,
                model.ImdbId ?? string.Empty,
                model.TmdbId,
                SpacetimeJson.Serialize(model.MalIds),
                SpacetimeJson.Serialize(model.AniListIds),
                model.Title ?? string.Empty,
                model.CleanTitle ?? string.Empty,
                model.SortTitle ?? string.Empty,
                (int)model.Status,
                model.Overview ?? string.Empty,
                model.AirTime ?? string.Empty,
                model.Monitored,
                (int)model.MonitorNewItems,
                model.QualityProfileId,
                model.SeasonFolder,
                SpacetimeDateTime.ToTimestamp(model.LastInfoSync),
                model.Runtime,
                SpacetimeJson.Serialize(model.Images),
                (int)model.SeriesType,
                model.Network ?? string.Empty,
                model.UseSceneNumbering,
                model.TitleSlug ?? string.Empty,
                model.Path ?? string.Empty,
                model.Year,
                SpacetimeJson.Serialize(model.Ratings),
                SpacetimeJson.Serialize(model.Genres),
                SpacetimeJson.Serialize(model.Actors),
                model.Certification ?? string.Empty,
                SpacetimeDateTime.ToTimestamp(model.Added),
                SpacetimeDateTime.ToTimestamp(model.FirstAired),
                SpacetimeDateTime.ToTimestamp(model.LastAired),
                SpacetimeJson.Serialize(model.OriginalLanguage),
                model.OriginalCountry ?? string.Empty,
                SpacetimeJson.Serialize(model.Seasons),
                SpacetimeJson.Serialize(model.AddOptions),
                (model.Tags ?? new HashSet<int>()).ToList());
        }

        protected override void InvokeUpdateReducer(Series model)
        {
            Conn.Connection.Reducers.UpdateSeries(
                model.Id,
                model.TvdbId,
                model.TvRageId,
                model.TvMazeId,
                model.ImdbId ?? string.Empty,
                model.TmdbId,
                SpacetimeJson.Serialize(model.MalIds),
                SpacetimeJson.Serialize(model.AniListIds),
                model.Title ?? string.Empty,
                model.CleanTitle ?? string.Empty,
                model.SortTitle ?? string.Empty,
                (int)model.Status,
                model.Overview ?? string.Empty,
                model.AirTime ?? string.Empty,
                model.Monitored,
                (int)model.MonitorNewItems,
                model.QualityProfileId,
                model.SeasonFolder,
                SpacetimeDateTime.ToTimestamp(model.LastInfoSync),
                model.Runtime,
                SpacetimeJson.Serialize(model.Images),
                (int)model.SeriesType,
                model.Network ?? string.Empty,
                model.UseSceneNumbering,
                model.TitleSlug ?? string.Empty,
                model.Path ?? string.Empty,
                model.Year,
                SpacetimeJson.Serialize(model.Ratings),
                SpacetimeJson.Serialize(model.Genres),
                SpacetimeJson.Serialize(model.Actors),
                model.Certification ?? string.Empty,
                SpacetimeDateTime.ToTimestamp(model.Added),
                SpacetimeDateTime.ToTimestamp(model.FirstAired),
                SpacetimeDateTime.ToTimestamp(model.LastAired),
                SpacetimeJson.Serialize(model.OriginalLanguage),
                model.OriginalCountry ?? string.Empty,
                SpacetimeJson.Serialize(model.Seasons),
                SpacetimeJson.Serialize(model.AddOptions),
                (model.Tags ?? new HashSet<int>()).ToList());
        }

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteSeries(id);

        public async Task<bool> SeriesPathExists(string path) => (await All()).Any(s => s.Path == path);

        public async Task<Series> FindByTitle(string cleanTitle)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            return ReturnSingleSeriesOrThrow((await All()).Where(s => s.CleanTitle == cleanTitle).ToList());
        }

        public async Task<Series> FindByTitle(string cleanTitle, int year)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            return ReturnSingleSeriesOrThrow((await All()).Where(s => s.CleanTitle == cleanTitle && s.Year == year).ToList());
        }

        // The real repository uses SQLite instr()/PostgreSQL strpos() to test whether each
        // series' CleanTitle is a substring of the caller's cleanTitle - neither exists in
        // SpacetimeDB's WHERE grammar (Phase 3 schema design). Fetch-all and test client-side;
        // acceptable at Sonarr's realistic series-count scale per that same decision.
        public async Task<List<Series>> FindByTitleInexact(string cleanTitle) =>
            (await All()).Where(s => cleanTitle.Contains(s.CleanTitle)).ToList();

        public async Task<Series> FindByTvdbId(int tvdbId) => (await All()).SingleOrDefault(s => s.TvdbId == tvdbId);

        public async Task<Series> FindByTvRageId(int tvRageId) => (await All()).SingleOrDefault(s => s.TvRageId == tvRageId);

        public async Task<Series> FindByImdbId(string imdbId) => (await All()).SingleOrDefault(s => s.ImdbId == imdbId);

        public async Task<Series> FindByPath(string path) => (await All()).FirstOrDefault(s => s.Path == path);

        public async Task<Dictionary<int, int>> AllSeriesTvdbIds() => (await All()).ToDictionary(s => s.Id, s => s.TvdbId);

        public async Task<Dictionary<int, string>> AllSeriesPaths() => (await All()).ToDictionary(s => s.Id, s => s.Path);

        public async Task<Dictionary<int, List<int>>> AllSeriesTags() =>
            (await All()).Where(s => s.Tags != null && s.Tags.Count > 0).ToDictionary(s => s.Id, s => s.Tags.ToList());

        public async Task<Dictionary<int, int>> AllSeriesQualityProfiles() => (await All()).ToDictionary(s => s.Id, s => s.QualityProfileId);

        private static Series ReturnSingleSeriesOrThrow(List<Series> series)
        {
            if (series.Count == 0)
            {
                return null;
            }

            if (series.Count == 1)
            {
                return series.First();
            }

            throw new MultipleSeriesFoundException(series, "Expected one series, but found {0}. Matching series: {1}", series.Count, string.Join(", ", series));
        }
    }
}
