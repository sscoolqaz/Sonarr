using SpacetimeDB;

public static partial class Module
{
    // One-time backfill reducers for cutting an existing SQLite-backed Sonarr install over to
    // SpacetimeDB. Unlike the normal insert_<entity> reducers, these accept the row's original
    // id explicitly instead of always assigning Id = 0 (auto-increment) - preserving ids across
    // the migration means every foreign key (Episode.SeriesId, EpisodeFile references, etc.)
    // transfers as-is with no in-script id-remapping table needed. Confirmed empirically that an
    // explicit-id insert doesn't disturb SpacetimeDB's own auto-increment sequence for later
    // normal inserts (they still get fresh, non-colliding ids afterward).
    //
    // Only for entities the first migration pass covers (Tag, QualityProfile, Series, Episode,
    // EpisodeFile, EpisodeHistory, Blocklist) - add more here as the migration tool's scope grows
    // to match the rest of the port. Not exposed through any repository - these exist only for
    // the migration script to call directly, once per row, in dependency order (parents before
    // children: Tag and QualityProfile first, then Series - followed by replace_series_tags,
    // already id-preserving since it takes seriesId/tagIds directly - then EpisodeFile, then
    // Episode, then EpisodeHistory and Blocklist last).
    [Reducer]
    public static void MigrateInsertTag(ReducerContext ctx, int id, string label)
    {
        ctx.Db.Tag.Insert(new Tag { Id = id, Label = label });
    }

    [Reducer]
    public static void MigrateInsertQualityProfile(
        ReducerContext ctx, int id, string name, bool upgradeAllowed, int cutoff, int minFormatScore,
        int cutoffFormatScore, int minUpgradeFormatScore, string formatItemsJson, string itemsJson)
    {
        ctx.Db.QualityProfile.Insert(new QualityProfile
        {
            Id = id, Name = name, UpgradeAllowed = upgradeAllowed, Cutoff = cutoff, MinFormatScore = minFormatScore,
            CutoffFormatScore = cutoffFormatScore, MinUpgradeFormatScore = minUpgradeFormatScore,
            FormatItemsJson = formatItemsJson, ItemsJson = itemsJson
        });
    }

    [Reducer]
    public static void MigrateInsertSeries(
        ReducerContext ctx, int id,
        int tvdbId, int tvRageId, int tvMazeId, string imdbId, int tmdbId, string malIdsJson, string aniListIdsJson,
        string title, string cleanTitle, string sortTitle, int status, string overview, string airTime,
        bool monitored, int monitorNewItems, int qualityProfileId, bool seasonFolder, Timestamp? lastInfoSync,
        int runtime, string imagesJson, int seriesType, string network, bool useSceneNumbering, string titleSlug,
        string path, int year, string ratingsJson, string genresJson, string actorsJson, string certification,
        Timestamp added, Timestamp? firstAired, Timestamp? lastAired, string originalLanguageJson,
        string originalCountry, string seasonsJson, string addOptionsJson)
    {
        ctx.Db.Series.Insert(new Series
        {
            Id = id, TvdbId = tvdbId, TvRageId = tvRageId, TvMazeId = tvMazeId, ImdbId = imdbId, TmdbId = tmdbId,
            MalIdsJson = malIdsJson, AniListIdsJson = aniListIdsJson, Title = title, CleanTitle = cleanTitle,
            SortTitle = sortTitle, Status = status, Overview = overview, AirTime = airTime, Monitored = monitored,
            MonitorNewItems = monitorNewItems, QualityProfileId = qualityProfileId, SeasonFolder = seasonFolder,
            LastInfoSync = lastInfoSync, Runtime = runtime, ImagesJson = imagesJson, SeriesType = seriesType,
            Network = network, UseSceneNumbering = useSceneNumbering, TitleSlug = titleSlug, Path = path,
            Year = year, RatingsJson = ratingsJson, GenresJson = genresJson, ActorsJson = actorsJson,
            Certification = certification, Added = added, FirstAired = firstAired, LastAired = lastAired,
            OriginalLanguageJson = originalLanguageJson, OriginalCountry = originalCountry,
            SeasonsJson = seasonsJson, AddOptionsJson = addOptionsJson
        });
    }

    [Reducer]
    public static void MigrateInsertEpisodeFile(
        ReducerContext ctx, int id, int seriesId, int seasonNumber, string relativePath, long size,
        Timestamp dateAdded, string originalFilePath, string sceneName, string releaseGroup, string releaseHash,
        string qualityJson, int qualityId, int indexerFlags, string mediaInfoJson, string languagesJson, int releaseType)
    {
        ctx.Db.EpisodeFile.Insert(new EpisodeFile
        {
            Id = id, SeriesId = seriesId, SeasonNumber = seasonNumber, RelativePath = relativePath, Size = size,
            DateAdded = dateAdded, OriginalFilePath = originalFilePath, SceneName = sceneName,
            ReleaseGroup = releaseGroup, ReleaseHash = releaseHash, QualityJson = qualityJson, QualityId = qualityId,
            IndexerFlags = indexerFlags, MediaInfoJson = mediaInfoJson, LanguagesJson = languagesJson,
            ReleaseType = releaseType
        });
    }

    [Reducer]
    public static void MigrateInsertEpisode(
        ReducerContext ctx, int id,
        int seriesId, int tvdbId, int episodeFileId, int seasonNumber, int episodeNumber, string title,
        string airDate, Timestamp? airDateUtc, string overview, bool monitored, int? absoluteEpisodeNumber,
        int? sceneAbsoluteEpisodeNumber, int? sceneSeasonNumber, int? sceneEpisodeNumber, int? airedAfterSeasonNumber,
        int? airedBeforeSeasonNumber, int? airedBeforeEpisodeNumber, bool unverifiedSceneNumbering, string ratingsJson,
        string imagesJson, Timestamp? lastSearchTime, int runtime, string finaleType)
    {
        ctx.Db.Episode.Insert(new Episode
        {
            Id = id, SeriesId = seriesId, TvdbId = tvdbId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber, Title = title, AirDate = airDate, AirDateUtc = airDateUtc, Overview = overview,
            Monitored = monitored, AbsoluteEpisodeNumber = absoluteEpisodeNumber, SceneAbsoluteEpisodeNumber = sceneAbsoluteEpisodeNumber,
            SceneSeasonNumber = sceneSeasonNumber, SceneEpisodeNumber = sceneEpisodeNumber, AiredAfterSeasonNumber = airedAfterSeasonNumber,
            AiredBeforeSeasonNumber = airedBeforeSeasonNumber, AiredBeforeEpisodeNumber = airedBeforeEpisodeNumber,
            UnverifiedSceneNumbering = unverifiedSceneNumbering, RatingsJson = ratingsJson, ImagesJson = imagesJson,
            LastSearchTime = lastSearchTime, Runtime = runtime, FinaleType = finaleType
        });
    }

    [Reducer]
    public static void MigrateInsertEpisodeHistory(
        ReducerContext ctx, int id,
        int episodeId, int seriesId, string sourceTitle, string qualityJson, int qualityId, Timestamp date,
        int eventType, string dataJson, string languagesJson, string downloadId)
    {
        ctx.Db.EpisodeHistory.Insert(new EpisodeHistory
        {
            Id = id, EpisodeId = episodeId, SeriesId = seriesId, SourceTitle = sourceTitle, QualityJson = qualityJson,
            QualityId = qualityId, Date = date, EventType = eventType, DataJson = dataJson, LanguagesJson = languagesJson,
            DownloadId = downloadId
        });
    }

    [Reducer]
    public static void MigrateInsertBlocklist(
        ReducerContext ctx, int id,
        int seriesId, string episodeIdsJson, string sourceTitle, string qualityJson, int qualityId, Timestamp date,
        Timestamp? publishedDate, long? size, int protocol, string indexer, int indexerFlags, int releaseType,
        string message, string source, string torrentInfoHash, string languagesJson)
    {
        ctx.Db.Blocklist.Insert(new Blocklist
        {
            Id = id, SeriesId = seriesId, EpisodeIdsJson = episodeIdsJson, SourceTitle = sourceTitle, QualityJson = qualityJson,
            QualityId = qualityId, Date = date, PublishedDate = publishedDate, Size = size, Protocol = protocol,
            Indexer = indexer, IndexerFlags = indexerFlags, ReleaseType = releaseType, Message = message, Source = source,
            TorrentInfoHash = torrentInfoHash, LanguagesJson = languagesJson
        });
    }
}
