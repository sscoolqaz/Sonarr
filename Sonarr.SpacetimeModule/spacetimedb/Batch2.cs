using SpacetimeDB;

public static partial class Module
{
    // --- DelayProfile --- (Tags stored as JSON string, encoded client-side)
    [Table(Accessor = "DelayProfile", Public = true)]
    public partial struct DelayProfile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public bool EnableUsenet;
        public bool EnableTorrent;
        public int PreferredProtocol;
        public int UsenetDelay;
        public int TorrentDelay;
        public int Order;
        public bool BypassIfHighestQuality;
        public bool BypassIfAboveCustomFormatScore;
        public int MinimumCustomFormatScore;
        public string TagsJson;
    }

    [Reducer]
    public static void InsertDelayProfile(ReducerContext ctx, bool enableUsenet, bool enableTorrent, int preferredProtocol, int usenetDelay, int torrentDelay, int order, bool bypassIfHighestQuality, bool bypassIfAboveCustomFormatScore, int minimumCustomFormatScore, string tagsJson)
    {
        ctx.Db.DelayProfile.Insert(new DelayProfile { Id = 0, EnableUsenet = enableUsenet, EnableTorrent = enableTorrent, PreferredProtocol = preferredProtocol, UsenetDelay = usenetDelay, TorrentDelay = torrentDelay, Order = order, BypassIfHighestQuality = bypassIfHighestQuality, BypassIfAboveCustomFormatScore = bypassIfAboveCustomFormatScore, MinimumCustomFormatScore = minimumCustomFormatScore, TagsJson = tagsJson });
    }

    [Reducer]
    public static void UpdateDelayProfile(ReducerContext ctx, int id, bool enableUsenet, bool enableTorrent, int preferredProtocol, int usenetDelay, int torrentDelay, int order, bool bypassIfHighestQuality, bool bypassIfAboveCustomFormatScore, int minimumCustomFormatScore, string tagsJson)
    {
        RequireFound(ctx.Db.DelayProfile.Id.Find(id).HasValue, "DelayProfile", id);
        ctx.Db.DelayProfile.Id.Update(new DelayProfile { Id = id, EnableUsenet = enableUsenet, EnableTorrent = enableTorrent, PreferredProtocol = preferredProtocol, UsenetDelay = usenetDelay, TorrentDelay = torrentDelay, Order = order, BypassIfHighestQuality = bypassIfHighestQuality, BypassIfAboveCustomFormatScore = bypassIfAboveCustomFormatScore, MinimumCustomFormatScore = minimumCustomFormatScore, TagsJson = tagsJson });
    }

    [Reducer]
    public static void DeleteDelayProfile(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.DelayProfile.Id.Delete(id), "DelayProfile", id);
    }

    // --- ReleaseProfile --- (list/set fields as JSON strings)
    [Table(Accessor = "ReleaseProfile", Public = true)]
    public partial struct ReleaseProfile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public bool Enabled;
        public string RequiredJson;
        public string IgnoredJson;
        public bool AirDateRestriction;
        public int AirDateGracePeriod;
        public bool AllowSeasonPackWithoutAllEpisodesAired;
        public string IndexerIdsJson;
        public string TagsJson;
        public string ExcludedTagsJson;
    }

    [Reducer]
    public static void InsertReleaseProfile(ReducerContext ctx, string name, bool enabled, string requiredJson, string ignoredJson, bool airDateRestriction, int airDateGracePeriod, bool allowSeasonPackWithoutAllEpisodesAired, string indexerIdsJson, string tagsJson, string excludedTagsJson)
    {
        ctx.Db.ReleaseProfile.Insert(new ReleaseProfile { Id = 0, Name = name, Enabled = enabled, RequiredJson = requiredJson, IgnoredJson = ignoredJson, AirDateRestriction = airDateRestriction, AirDateGracePeriod = airDateGracePeriod, AllowSeasonPackWithoutAllEpisodesAired = allowSeasonPackWithoutAllEpisodesAired, IndexerIdsJson = indexerIdsJson, TagsJson = tagsJson, ExcludedTagsJson = excludedTagsJson });
    }

    [Reducer]
    public static void UpdateReleaseProfile(ReducerContext ctx, int id, string name, bool enabled, string requiredJson, string ignoredJson, bool airDateRestriction, int airDateGracePeriod, bool allowSeasonPackWithoutAllEpisodesAired, string indexerIdsJson, string tagsJson, string excludedTagsJson)
    {
        RequireFound(ctx.Db.ReleaseProfile.Id.Find(id).HasValue, "ReleaseProfile", id);
        ctx.Db.ReleaseProfile.Id.Update(new ReleaseProfile { Id = id, Name = name, Enabled = enabled, RequiredJson = requiredJson, IgnoredJson = ignoredJson, AirDateRestriction = airDateRestriction, AirDateGracePeriod = airDateGracePeriod, AllowSeasonPackWithoutAllEpisodesAired = allowSeasonPackWithoutAllEpisodesAired, IndexerIdsJson = indexerIdsJson, TagsJson = tagsJson, ExcludedTagsJson = excludedTagsJson });
    }

    [Reducer]
    public static void DeleteReleaseProfile(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.ReleaseProfile.Id.Delete(id), "ReleaseProfile", id);
    }

    // --- QualityDefinition --- (only Quality + Title are actually persisted per TableMapping.cs;
    // GroupName/Weight/MinSize/MaxSize/PreferredSize are Ignore()'d there too)
    [Table(Accessor = "QualityDefinition", Public = true)]
    public partial struct QualityDefinition
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string QualityJson;
        public string Title;
    }

    [Reducer]
    public static void InsertQualityDefinition(ReducerContext ctx, string qualityJson, string title)
    {
        ctx.Db.QualityDefinition.Insert(new QualityDefinition { Id = 0, QualityJson = qualityJson, Title = title });
    }

    [Reducer]
    public static void UpdateQualityDefinition(ReducerContext ctx, int id, string qualityJson, string title)
    {
        RequireFound(ctx.Db.QualityDefinition.Id.Find(id).HasValue, "QualityDefinition", id);
        ctx.Db.QualityDefinition.Id.Update(new QualityDefinition { Id = id, QualityJson = qualityJson, Title = title });
    }

    [Reducer]
    public static void DeleteQualityDefinition(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.QualityDefinition.Id.Delete(id), "QualityDefinition", id);
    }

    // --- NamingConfig ---
    [Table(Accessor = "NamingConfig", Public = true)]
    public partial struct NamingConfig
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public bool RenameEpisodes;
        public bool ReplaceIllegalCharacters;
        public int ColonReplacementFormat;
        public string CustomColonReplacementFormat;
        public int MultiEpisodeStyle;
        public string StandardEpisodeFormat;
        public string DailyEpisodeFormat;
        public string AnimeEpisodeFormat;
        public string SeriesFolderFormat;
        public string SeasonFolderFormat;
        public string SpecialsFolderFormat;
    }

    [Reducer]
    public static void InsertNamingConfig(ReducerContext ctx, bool renameEpisodes, bool replaceIllegalCharacters, int colonReplacementFormat, string customColonReplacementFormat, int multiEpisodeStyle, string standardEpisodeFormat, string dailyEpisodeFormat, string animeEpisodeFormat, string seriesFolderFormat, string seasonFolderFormat, string specialsFolderFormat)
    {
        ctx.Db.NamingConfig.Insert(new NamingConfig { Id = 0, RenameEpisodes = renameEpisodes, ReplaceIllegalCharacters = replaceIllegalCharacters, ColonReplacementFormat = colonReplacementFormat, CustomColonReplacementFormat = customColonReplacementFormat, MultiEpisodeStyle = multiEpisodeStyle, StandardEpisodeFormat = standardEpisodeFormat, DailyEpisodeFormat = dailyEpisodeFormat, AnimeEpisodeFormat = animeEpisodeFormat, SeriesFolderFormat = seriesFolderFormat, SeasonFolderFormat = seasonFolderFormat, SpecialsFolderFormat = specialsFolderFormat });
    }

    [Reducer]
    public static void UpdateNamingConfig(ReducerContext ctx, int id, bool renameEpisodes, bool replaceIllegalCharacters, int colonReplacementFormat, string customColonReplacementFormat, int multiEpisodeStyle, string standardEpisodeFormat, string dailyEpisodeFormat, string animeEpisodeFormat, string seriesFolderFormat, string seasonFolderFormat, string specialsFolderFormat)
    {
        RequireFound(ctx.Db.NamingConfig.Id.Find(id).HasValue, "NamingConfig", id);
        ctx.Db.NamingConfig.Id.Update(new NamingConfig { Id = id, RenameEpisodes = renameEpisodes, ReplaceIllegalCharacters = replaceIllegalCharacters, ColonReplacementFormat = colonReplacementFormat, CustomColonReplacementFormat = customColonReplacementFormat, MultiEpisodeStyle = multiEpisodeStyle, StandardEpisodeFormat = standardEpisodeFormat, DailyEpisodeFormat = dailyEpisodeFormat, AnimeEpisodeFormat = animeEpisodeFormat, SeriesFolderFormat = seriesFolderFormat, SeasonFolderFormat = seasonFolderFormat, SpecialsFolderFormat = specialsFolderFormat });
    }

    [Reducer]
    public static void DeleteNamingConfig(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.NamingConfig.Id.Delete(id), "NamingConfig", id);
    }

    // --- SceneMapping ---
    [Table(Accessor = "SceneMapping", Public = true)]
    public partial struct SceneMapping
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string MappingId;
        public string Title;
        public string ParseTerm;
        public string SearchTerm;
        public int TvdbId;
        public int? SeasonNumber;
        public int? SceneSeasonNumber;
        public string SceneOrigin;
        public int? SearchMode;
        public string Comment;
        public string FilterRegex;
        public string Type;
    }

    [Reducer]
    public static void InsertSceneMapping(ReducerContext ctx, string mappingId, string title, string parseTerm, string searchTerm, int tvdbId, int? seasonNumber, int? sceneSeasonNumber, string sceneOrigin, int? searchMode, string comment, string filterRegex, string type)
    {
        ctx.Db.SceneMapping.Insert(new SceneMapping { Id = 0, MappingId = mappingId, Title = title, ParseTerm = parseTerm, SearchTerm = searchTerm, TvdbId = tvdbId, SeasonNumber = seasonNumber, SceneSeasonNumber = sceneSeasonNumber, SceneOrigin = sceneOrigin, SearchMode = searchMode, Comment = comment, FilterRegex = filterRegex, Type = type });
    }

    [Reducer]
    public static void UpdateSceneMapping(ReducerContext ctx, int id, string mappingId, string title, string parseTerm, string searchTerm, int tvdbId, int? seasonNumber, int? sceneSeasonNumber, string sceneOrigin, int? searchMode, string comment, string filterRegex, string type)
    {
        RequireFound(ctx.Db.SceneMapping.Id.Find(id).HasValue, "SceneMapping", id);
        ctx.Db.SceneMapping.Id.Update(new SceneMapping { Id = id, MappingId = mappingId, Title = title, ParseTerm = parseTerm, SearchTerm = searchTerm, TvdbId = tvdbId, SeasonNumber = seasonNumber, SceneSeasonNumber = sceneSeasonNumber, SceneOrigin = sceneOrigin, SearchMode = searchMode, Comment = comment, FilterRegex = filterRegex, Type = type });
    }

    [Reducer]
    public static void DeleteSceneMapping(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.SceneMapping.Id.Delete(id), "SceneMapping", id);
    }

    // --- ScheduledTask ---
    [Table(Accessor = "ScheduledTask", Public = true)]
    public partial struct ScheduledTask
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string TypeName;
        public int Interval;
        public Timestamp LastExecution;
        public int Priority;
        public Timestamp LastStartTime;
    }

    [Reducer]
    public static void InsertScheduledTask(ReducerContext ctx, string typeName, int interval, Timestamp lastExecution, int priority, Timestamp lastStartTime)
    {
        ctx.Db.ScheduledTask.Insert(new ScheduledTask { Id = 0, TypeName = typeName, Interval = interval, LastExecution = lastExecution, Priority = priority, LastStartTime = lastStartTime });
    }

    [Reducer]
    public static void UpdateScheduledTask(ReducerContext ctx, int id, string typeName, int interval, Timestamp lastExecution, int priority, Timestamp lastStartTime)
    {
        RequireFound(ctx.Db.ScheduledTask.Id.Find(id).HasValue, "ScheduledTask", id);
        ctx.Db.ScheduledTask.Id.Update(new ScheduledTask { Id = id, TypeName = typeName, Interval = interval, LastExecution = lastExecution, Priority = priority, LastStartTime = lastStartTime });
    }

    [Reducer]
    public static void DeleteScheduledTask(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.ScheduledTask.Id.Delete(id), "ScheduledTask", id);
    }
}
