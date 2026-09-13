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

    // Second migration pass - extends the same id-preserving pattern above to the rest of the
    // port's entities, in dependency order (config/singletons and reference tables first,
    // provider definitions next since Status tables aren't migrated - see the migration tool's
    // own comment for what's deliberately left out and why - then per-series extra file tables).
    [Reducer]
    public static void MigrateInsertCustomFormat(ReducerContext ctx, int id, string name, bool includeCustomFormatWhenRenaming, string specificationsJson)
    {
        ctx.Db.CustomFormat.Insert(new CustomFormat { Id = id, Name = name, IncludeCustomFormatWhenRenaming = includeCustomFormatWhenRenaming, SpecificationsJson = specificationsJson });
    }

    [Reducer]
    public static void MigrateInsertConfig(ReducerContext ctx, int id, string key, string value)
    {
        ctx.Db.Config.Insert(new Config { Id = id, Key = key, Value = value });
    }

    [Reducer]
    public static void MigrateInsertNamingConfig(
        ReducerContext ctx, int id, bool renameEpisodes, bool replaceIllegalCharacters, int colonReplacementFormat,
        string customColonReplacementFormat, int multiEpisodeStyle, string standardEpisodeFormat, string dailyEpisodeFormat,
        string animeEpisodeFormat, string seriesFolderFormat, string seasonFolderFormat, string specialsFolderFormat)
    {
        ctx.Db.NamingConfig.Insert(new NamingConfig
        {
            Id = id, RenameEpisodes = renameEpisodes, ReplaceIllegalCharacters = replaceIllegalCharacters,
            ColonReplacementFormat = colonReplacementFormat, CustomColonReplacementFormat = customColonReplacementFormat,
            MultiEpisodeStyle = multiEpisodeStyle, StandardEpisodeFormat = standardEpisodeFormat,
            DailyEpisodeFormat = dailyEpisodeFormat, AnimeEpisodeFormat = animeEpisodeFormat,
            SeriesFolderFormat = seriesFolderFormat, SeasonFolderFormat = seasonFolderFormat,
            SpecialsFolderFormat = specialsFolderFormat
        });
    }

    [Reducer]
    public static void MigrateInsertRootFolder(ReducerContext ctx, int id, string path)
    {
        ctx.Db.RootFolder.Insert(new RootFolder { Id = id, Path = path });
    }

    [Reducer]
    public static void MigrateInsertRemotePathMapping(ReducerContext ctx, int id, string host, string remotePath, string localPath)
    {
        ctx.Db.RemotePathMapping.Insert(new RemotePathMapping { Id = id, Host = host, RemotePath = remotePath, LocalPath = localPath });
    }

    [Reducer]
    public static void MigrateInsertCustomFilter(ReducerContext ctx, int id, string type, string label, string filters)
    {
        ctx.Db.CustomFilter.Insert(new CustomFilter { Id = id, Type = type, Label = label, Filters = filters });
    }

    [Reducer]
    public static void MigrateInsertDelayProfile(
        ReducerContext ctx, int id, bool enableUsenet, bool enableTorrent, int preferredProtocol, int usenetDelay,
        int torrentDelay, int order, bool bypassIfHighestQuality, bool bypassIfAboveCustomFormatScore,
        int minimumCustomFormatScore, string tagsJson)
    {
        ctx.Db.DelayProfile.Insert(new DelayProfile
        {
            Id = id, EnableUsenet = enableUsenet, EnableTorrent = enableTorrent, PreferredProtocol = preferredProtocol,
            UsenetDelay = usenetDelay, TorrentDelay = torrentDelay, Order = order, BypassIfHighestQuality = bypassIfHighestQuality,
            BypassIfAboveCustomFormatScore = bypassIfAboveCustomFormatScore, MinimumCustomFormatScore = minimumCustomFormatScore,
            TagsJson = tagsJson
        });
    }

    [Reducer]
    public static void MigrateInsertReleaseProfile(
        ReducerContext ctx, int id, string name, bool enabled, string requiredJson, string ignoredJson,
        bool airDateRestriction, int airDateGracePeriod, bool allowSeasonPackWithoutAllEpisodesAired,
        string indexerIdsJson, string tagsJson, string excludedTagsJson)
    {
        ctx.Db.ReleaseProfile.Insert(new ReleaseProfile
        {
            Id = id, Name = name, Enabled = enabled, RequiredJson = requiredJson, IgnoredJson = ignoredJson,
            AirDateRestriction = airDateRestriction, AirDateGracePeriod = airDateGracePeriod,
            AllowSeasonPackWithoutAllEpisodesAired = allowSeasonPackWithoutAllEpisodesAired,
            IndexerIdsJson = indexerIdsJson, TagsJson = tagsJson, ExcludedTagsJson = excludedTagsJson
        });
    }

    [Reducer]
    public static void MigrateInsertIndexerDefinition(
        ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson,
        bool enable, string tagsJson, string messageJson)
    {
        ctx.Db.IndexerDefinition.Insert(new IndexerDefinition
        {
            Id = id, Name = name, Implementation = implementation, ConfigContract = configContract,
            SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson
        });
    }

    [Reducer]
    public static void MigrateInsertDownloadClientDefinition(
        ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson,
        bool enable, string tagsJson, string messageJson)
    {
        ctx.Db.DownloadClientDefinition.Insert(new DownloadClientDefinition
        {
            Id = id, Name = name, Implementation = implementation, ConfigContract = configContract,
            SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson
        });
    }

    [Reducer]
    public static void MigrateInsertImportListDefinition(
        ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson,
        bool enable, string tagsJson, string messageJson)
    {
        ctx.Db.ImportListDefinition.Insert(new ImportListDefinition
        {
            Id = id, Name = name, Implementation = implementation, ConfigContract = configContract,
            SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson
        });
    }

    [Reducer]
    public static void MigrateInsertNotificationDefinition(
        ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson,
        bool enable, string tagsJson, string messageJson)
    {
        ctx.Db.NotificationDefinition.Insert(new NotificationDefinition
        {
            Id = id, Name = name, Implementation = implementation, ConfigContract = configContract,
            SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson
        });
    }

    [Reducer]
    public static void MigrateInsertMetadataDefinition(
        ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson,
        bool enable, string tagsJson, string messageJson)
    {
        ctx.Db.MetadataDefinition.Insert(new MetadataDefinition
        {
            Id = id, Name = name, Implementation = implementation, ConfigContract = configContract,
            SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson
        });
    }

    [Reducer]
    public static void MigrateInsertImportListExclusion(ReducerContext ctx, int id, int tvdbId, string title)
    {
        ctx.Db.ImportListExclusion.Insert(new ImportListExclusion { Id = id, TvdbId = tvdbId, Title = title });
    }

    [Reducer]
    public static void MigrateInsertQualityDefinition(ReducerContext ctx, int id, string qualityJson, string title)
    {
        ctx.Db.QualityDefinition.Insert(new QualityDefinition { Id = id, QualityJson = qualityJson, Title = title });
    }

    [Reducer]
    public static void MigrateInsertAutoTag(
        ReducerContext ctx, int id, string name, string specificationsJson, bool removeTagsAutomatically, string tagsJson)
    {
        ctx.Db.AutoTag.Insert(new AutoTag { Id = id, Name = name, SpecificationsJson = specificationsJson, RemoveTagsAutomatically = removeTagsAutomatically, TagsJson = tagsJson });
    }

    [Reducer]
    public static void MigrateInsertUser(ReducerContext ctx, int id, string identifier, string username, string password, string salt, int iterations)
    {
        ctx.Db.User.Insert(new User { Id = id, Identifier = identifier, Username = username, Password = password, Salt = salt, Iterations = iterations });
    }

    [Reducer]
    public static void MigrateInsertUpdateHistory(ReducerContext ctx, int id, Timestamp date, string version, int eventType)
    {
        ctx.Db.UpdateHistory.Insert(new UpdateHistory { Id = id, Date = date, Version = version, EventType = eventType });
    }

    [Reducer]
    public static void MigrateInsertPendingRelease(
        ReducerContext ctx, int id, int seriesId, string title, Timestamp added, string parsedEpisodeInfoJson,
        string releaseJson, int reason, string additionalInfoJson)
    {
        ctx.Db.PendingRelease.Insert(new PendingRelease
        {
            Id = id, SeriesId = seriesId, Title = title, Added = added, ParsedEpisodeInfoJson = parsedEpisodeInfoJson,
            ReleaseJson = releaseJson, Reason = reason, AdditionalInfoJson = additionalInfoJson
        });
    }

    [Reducer]
    public static void MigrateInsertDownloadHistory(
        ReducerContext ctx, int id, int eventType, int seriesId, string downloadId, string sourceTitle, Timestamp date,
        int protocol, int indexerId, int downloadClientId, string releaseJson, string dataJson)
    {
        ctx.Db.DownloadHistory.Insert(new DownloadHistory
        {
            Id = id, EventType = eventType, SeriesId = seriesId, DownloadId = downloadId, SourceTitle = sourceTitle,
            Date = date, Protocol = protocol, IndexerId = indexerId, DownloadClientId = downloadClientId,
            ReleaseJson = releaseJson, DataJson = dataJson
        });
    }

    [Reducer]
    public static void MigrateInsertMetadataFile(
        ReducerContext ctx, int id, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath,
        Timestamp added, Timestamp lastUpdated, string extension, string hash, string consumer, int type)
    {
        ctx.Db.MetadataFile.Insert(new MetadataFile
        {
            Id = id, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber,
            RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension,
            Hash = hash, Consumer = consumer, Type = type
        });
    }

    [Reducer]
    public static void MigrateInsertSubtitleFile(
        ReducerContext ctx, int id, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath,
        Timestamp added, Timestamp lastUpdated, string extension, string languageJson, int copy, string languageTagsJson, string title)
    {
        ctx.Db.SubtitleFile.Insert(new SubtitleFile
        {
            Id = id, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber,
            RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension,
            LanguageJson = languageJson, Copy = copy, LanguageTagsJson = languageTagsJson, Title = title
        });
    }

    [Reducer]
    public static void MigrateInsertOtherExtraFile(
        ReducerContext ctx, int id, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath,
        Timestamp added, Timestamp lastUpdated, string extension)
    {
        ctx.Db.OtherExtraFile.Insert(new OtherExtraFile
        {
            Id = id, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber,
            RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension
        });
    }

    [Reducer]
    public static void MigrateInsertScheduledTask(
        ReducerContext ctx, int id, string typeName, int interval, Timestamp lastExecution, int priority, Timestamp lastStartTime)
    {
        ctx.Db.ScheduledTask.Insert(new ScheduledTask
        {
            Id = id, TypeName = typeName, Interval = interval, LastExecution = lastExecution, Priority = priority,
            LastStartTime = lastStartTime
        });
    }
}
