using SpacetimeDB;

public static partial class Module
{
    // --- Episode (Tier 2). Series/EpisodeFile navigation properties are not persisted here,
    // same as the real TableMapping (.Ignore(e => e.Series), EpisodeFile is LazyLoaded) - callers
    // get them via SpacetimeEpisodeRepository's join helpers, not stored columns.
    [Table(Accessor = "Episode", Public = true)]
    public partial struct Episode
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public int TvdbId;
        public int EpisodeFileId;
        public int SeasonNumber;
        public int EpisodeNumber;
        public string Title;
        public string AirDate;
        public Timestamp? AirDateUtc;
        public string Overview;
        public bool Monitored;
        public int? AbsoluteEpisodeNumber;
        public int? SceneAbsoluteEpisodeNumber;
        public int? SceneSeasonNumber;
        public int? SceneEpisodeNumber;
        public int? AiredAfterSeasonNumber;
        public int? AiredBeforeSeasonNumber;
        public int? AiredBeforeEpisodeNumber;
        public bool UnverifiedSceneNumbering;
        public string RatingsJson;
        public string ImagesJson;
        public Timestamp? LastSearchTime;
        public int Runtime;
        public string FinaleType;
    }

    [Reducer]
    public static void InsertEpisode(
        ReducerContext ctx,
        int seriesId, int tvdbId, int episodeFileId, int seasonNumber, int episodeNumber, string title,
        string airDate, Timestamp? airDateUtc, string overview, bool monitored, int? absoluteEpisodeNumber,
        int? sceneAbsoluteEpisodeNumber, int? sceneSeasonNumber, int? sceneEpisodeNumber, int? airedAfterSeasonNumber,
        int? airedBeforeSeasonNumber, int? airedBeforeEpisodeNumber, bool unverifiedSceneNumbering, string ratingsJson,
        string imagesJson, Timestamp? lastSearchTime, int runtime, string finaleType)
    {
        RequireAuth(ctx);
        ctx.Db.Episode.Insert(new Episode
        {
            Id = 0, SeriesId = seriesId, TvdbId = tvdbId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber, Title = title, AirDate = airDate, AirDateUtc = airDateUtc, Overview = overview,
            Monitored = monitored, AbsoluteEpisodeNumber = absoluteEpisodeNumber, SceneAbsoluteEpisodeNumber = sceneAbsoluteEpisodeNumber,
            SceneSeasonNumber = sceneSeasonNumber, SceneEpisodeNumber = sceneEpisodeNumber, AiredAfterSeasonNumber = airedAfterSeasonNumber,
            AiredBeforeSeasonNumber = airedBeforeSeasonNumber, AiredBeforeEpisodeNumber = airedBeforeEpisodeNumber,
            UnverifiedSceneNumbering = unverifiedSceneNumbering, RatingsJson = ratingsJson, ImagesJson = imagesJson,
            LastSearchTime = lastSearchTime, Runtime = runtime, FinaleType = finaleType
        });
    }

    [Reducer]
    public static void UpdateEpisode(
        ReducerContext ctx,
        int id, int seriesId, int tvdbId, int episodeFileId, int seasonNumber, int episodeNumber, string title,
        string airDate, Timestamp? airDateUtc, string overview, bool monitored, int? absoluteEpisodeNumber,
        int? sceneAbsoluteEpisodeNumber, int? sceneSeasonNumber, int? sceneEpisodeNumber, int? airedAfterSeasonNumber,
        int? airedBeforeSeasonNumber, int? airedBeforeEpisodeNumber, bool unverifiedSceneNumbering, string ratingsJson,
        string imagesJson, Timestamp? lastSearchTime, int runtime, string finaleType)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Episode.Id.Find(id).HasValue, "Episode", id);
        ctx.Db.Episode.Id.Update(new Episode
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
    public static void DeleteEpisode(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Episode.Id.Delete(id), "Episode", id);
    }

    // --- EpisodeHistory (Tier 2, real SQL table name "History"). QualityId is the same
    // Phase 3-designed normalized column as EpisodeFile's - derived by the repository from the
    // submitted Quality document at exactly one choke point (SpacetimeHistoryRepository's
    // InvokeInsertReducer/InvokeUpdateReducer), never accepted independently here, same
    // established pattern as SpacetimeMediaFileRepository.DeriveQualityId.
    [Table(Accessor = "EpisodeHistory", Public = true)]
    public partial struct EpisodeHistory
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int EpisodeId;
        public int SeriesId;
        public string SourceTitle;
        public string QualityJson;
        public int QualityId;
        public Timestamp Date;
        public int EventType;
        public string DataJson;
        public string LanguagesJson;
        public string DownloadId;
    }

    [Reducer]
    public static void InsertEpisodeHistory(
        ReducerContext ctx,
        int episodeId, int seriesId, string sourceTitle, string qualityJson, int qualityId, Timestamp date,
        int eventType, string dataJson, string languagesJson, string downloadId)
    {
        RequireAuth(ctx);
        ctx.Db.EpisodeHistory.Insert(new EpisodeHistory
        {
            Id = 0, EpisodeId = episodeId, SeriesId = seriesId, SourceTitle = sourceTitle, QualityJson = qualityJson,
            QualityId = qualityId, Date = date, EventType = eventType, DataJson = dataJson, LanguagesJson = languagesJson,
            DownloadId = downloadId
        });
    }

    [Reducer]
    public static void UpdateEpisodeHistory(
        ReducerContext ctx,
        int id, int episodeId, int seriesId, string sourceTitle, string qualityJson, int qualityId, Timestamp date,
        int eventType, string dataJson, string languagesJson, string downloadId)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.EpisodeHistory.Id.Find(id).HasValue, "EpisodeHistory", id);
        ctx.Db.EpisodeHistory.Id.Update(new EpisodeHistory
        {
            Id = id, EpisodeId = episodeId, SeriesId = seriesId, SourceTitle = sourceTitle, QualityJson = qualityJson,
            QualityId = qualityId, Date = date, EventType = eventType, DataJson = dataJson, LanguagesJson = languagesJson,
            DownloadId = downloadId
        });
    }

    [Reducer]
    public static void DeleteEpisodeHistory(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.EpisodeHistory.Id.Delete(id), "EpisodeHistory", id);
    }

    // --- Blocklist (Tier 2). Same QualityId derivation invariant as EpisodeHistory above.
    [Table(Accessor = "Blocklist", Public = true)]
    public partial struct Blocklist
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public string EpisodeIdsJson;
        public string SourceTitle;
        public string QualityJson;
        public int QualityId;
        public Timestamp Date;
        public Timestamp? PublishedDate;
        public long? Size;
        public int Protocol;
        public string Indexer;
        public int IndexerFlags;
        public int ReleaseType;
        public string Message;
        public string Source;
        public string TorrentInfoHash;
        public string LanguagesJson;
    }

    [Reducer]
    public static void InsertBlocklist(
        ReducerContext ctx,
        int seriesId, string episodeIdsJson, string sourceTitle, string qualityJson, int qualityId, Timestamp date,
        Timestamp? publishedDate, long? size, int protocol, string indexer, int indexerFlags, int releaseType,
        string message, string source, string torrentInfoHash, string languagesJson)
    {
        RequireAuth(ctx);
        ctx.Db.Blocklist.Insert(new Blocklist
        {
            Id = 0, SeriesId = seriesId, EpisodeIdsJson = episodeIdsJson, SourceTitle = sourceTitle, QualityJson = qualityJson,
            QualityId = qualityId, Date = date, PublishedDate = publishedDate, Size = size, Protocol = protocol,
            Indexer = indexer, IndexerFlags = indexerFlags, ReleaseType = releaseType, Message = message, Source = source,
            TorrentInfoHash = torrentInfoHash, LanguagesJson = languagesJson
        });
    }

    [Reducer]
    public static void UpdateBlocklist(
        ReducerContext ctx,
        int id, int seriesId, string episodeIdsJson, string sourceTitle, string qualityJson, int qualityId, Timestamp date,
        Timestamp? publishedDate, long? size, int protocol, string indexer, int indexerFlags, int releaseType,
        string message, string source, string torrentInfoHash, string languagesJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Blocklist.Id.Find(id).HasValue, "Blocklist", id);
        ctx.Db.Blocklist.Id.Update(new Blocklist
        {
            Id = id, SeriesId = seriesId, EpisodeIdsJson = episodeIdsJson, SourceTitle = sourceTitle, QualityJson = qualityJson,
            QualityId = qualityId, Date = date, PublishedDate = publishedDate, Size = size, Protocol = protocol,
            Indexer = indexer, IndexerFlags = indexerFlags, ReleaseType = releaseType, Message = message, Source = source,
            TorrentInfoHash = torrentInfoHash, LanguagesJson = languagesJson
        });
    }

    [Reducer]
    public static void DeleteBlocklist(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Blocklist.Id.Delete(id), "Blocklist", id);
    }
}
