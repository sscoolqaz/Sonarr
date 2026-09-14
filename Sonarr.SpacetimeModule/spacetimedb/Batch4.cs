using SpacetimeDB;

public static partial class Module
{
    // --- ExtraFile family: OtherExtraFile, SubtitleFile, MetadataFile ---
    // Shared base fields (SeriesId, EpisodeFileId, SeasonNumber, RelativePath, Added,
    // LastUpdated, Extension) repeated per table since SpacetimeDB tables can't inherit columns.

    [Table(Accessor = "OtherExtraFile", Public = true)]
    public partial struct OtherExtraFile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public int? EpisodeFileId;
        public int? SeasonNumber;
        public string RelativePath;
        public Timestamp Added;
        public Timestamp LastUpdated;
        public string Extension;
    }

    [Reducer]
    public static void InsertOtherExtraFile(ReducerContext ctx, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath, Timestamp added, Timestamp lastUpdated, string extension)
    {
        RequireAuth(ctx);
        ctx.Db.OtherExtraFile.Insert(new OtherExtraFile { Id = 0, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber, RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension });
    }

    [Reducer]
    public static void UpdateOtherExtraFile(ReducerContext ctx, int id, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath, Timestamp added, Timestamp lastUpdated, string extension)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.OtherExtraFile.Id.Find(id).HasValue, "OtherExtraFile", id);
        ctx.Db.OtherExtraFile.Id.Update(new OtherExtraFile { Id = id, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber, RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension });
    }

    [Reducer]
    public static void DeleteOtherExtraFile(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.OtherExtraFile.Id.Delete(id), "OtherExtraFile", id);
    }

    [Table(Accessor = "SubtitleFile", Public = true)]
    public partial struct SubtitleFile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public int? EpisodeFileId;
        public int? SeasonNumber;
        public string RelativePath;
        public Timestamp Added;
        public Timestamp LastUpdated;
        public string Extension;
        public string LanguageJson;
        public int Copy;
        public string LanguageTagsJson;
        public string Title;
    }

    [Reducer]
    public static void InsertSubtitleFile(ReducerContext ctx, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath, Timestamp added, Timestamp lastUpdated, string extension, string languageJson, int copy, string languageTagsJson, string title)
    {
        RequireAuth(ctx);
        ctx.Db.SubtitleFile.Insert(new SubtitleFile { Id = 0, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber, RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension, LanguageJson = languageJson, Copy = copy, LanguageTagsJson = languageTagsJson, Title = title });
    }

    [Reducer]
    public static void UpdateSubtitleFile(ReducerContext ctx, int id, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath, Timestamp added, Timestamp lastUpdated, string extension, string languageJson, int copy, string languageTagsJson, string title)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.SubtitleFile.Id.Find(id).HasValue, "SubtitleFile", id);
        ctx.Db.SubtitleFile.Id.Update(new SubtitleFile { Id = id, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber, RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension, LanguageJson = languageJson, Copy = copy, LanguageTagsJson = languageTagsJson, Title = title });
    }

    [Reducer]
    public static void DeleteSubtitleFile(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.SubtitleFile.Id.Delete(id), "SubtitleFile", id);
    }

    [Table(Accessor = "MetadataFile", Public = true)]
    public partial struct MetadataFile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public int? EpisodeFileId;
        public int? SeasonNumber;
        public string RelativePath;
        public Timestamp Added;
        public Timestamp LastUpdated;
        public string Extension;
        public string Hash;
        public string Consumer;
        public int Type;
    }

    [Reducer]
    public static void InsertMetadataFile(ReducerContext ctx, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath, Timestamp added, Timestamp lastUpdated, string extension, string hash, string consumer, int type)
    {
        RequireAuth(ctx);
        ctx.Db.MetadataFile.Insert(new MetadataFile { Id = 0, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber, RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension, Hash = hash, Consumer = consumer, Type = type });
    }

    [Reducer]
    public static void UpdateMetadataFile(ReducerContext ctx, int id, int seriesId, int? episodeFileId, int? seasonNumber, string relativePath, Timestamp added, Timestamp lastUpdated, string extension, string hash, string consumer, int type)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.MetadataFile.Id.Find(id).HasValue, "MetadataFile", id);
        ctx.Db.MetadataFile.Id.Update(new MetadataFile { Id = id, SeriesId = seriesId, EpisodeFileId = episodeFileId, SeasonNumber = seasonNumber, RelativePath = relativePath, Added = added, LastUpdated = lastUpdated, Extension = extension, Hash = hash, Consumer = consumer, Type = type });
    }

    [Reducer]
    public static void DeleteMetadataFile(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.MetadataFile.Id.Delete(id), "MetadataFile", id);
    }

    // --- ProviderStatus family: IndexerStatus, DownloadClientStatus, ImportListStatus, NotificationStatus ---
    [Table(Accessor = "IndexerStatus", Public = true)]
    public partial struct IndexerStatus
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int ProviderId;
        public Timestamp? InitialFailure;
        public Timestamp? MostRecentFailure;
        public int EscalationLevel;
        public Timestamp? DisabledTill;
        public string LastRssSyncReleaseInfoJson;
    }

    [Reducer]
    public static void InsertIndexerStatus(ReducerContext ctx, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill, string lastRssSyncReleaseInfoJson)
    {
        RequireAuth(ctx);
        ctx.Db.IndexerStatus.Insert(new IndexerStatus { Id = 0, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill, LastRssSyncReleaseInfoJson = lastRssSyncReleaseInfoJson });
    }

    [Reducer]
    public static void UpdateIndexerStatus(ReducerContext ctx, int id, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill, string lastRssSyncReleaseInfoJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.IndexerStatus.Id.Find(id).HasValue, "IndexerStatus", id);
        ctx.Db.IndexerStatus.Id.Update(new IndexerStatus { Id = id, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill, LastRssSyncReleaseInfoJson = lastRssSyncReleaseInfoJson });
    }

    [Reducer]
    public static void DeleteIndexerStatus(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.IndexerStatus.Id.Delete(id), "IndexerStatus", id);
    }

    [Table(Accessor = "DownloadClientStatus", Public = true)]
    public partial struct DownloadClientStatus
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int ProviderId;
        public Timestamp? InitialFailure;
        public Timestamp? MostRecentFailure;
        public int EscalationLevel;
        public Timestamp? DisabledTill;
    }

    [Reducer]
    public static void InsertDownloadClientStatus(ReducerContext ctx, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill)
    {
        RequireAuth(ctx);
        ctx.Db.DownloadClientStatus.Insert(new DownloadClientStatus { Id = 0, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill });
    }

    [Reducer]
    public static void UpdateDownloadClientStatus(ReducerContext ctx, int id, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.DownloadClientStatus.Id.Find(id).HasValue, "DownloadClientStatus", id);
        ctx.Db.DownloadClientStatus.Id.Update(new DownloadClientStatus { Id = id, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill });
    }

    [Reducer]
    public static void DeleteDownloadClientStatus(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.DownloadClientStatus.Id.Delete(id), "DownloadClientStatus", id);
    }

    [Table(Accessor = "ImportListStatus", Public = true)]
    public partial struct ImportListStatus
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int ProviderId;
        public Timestamp? InitialFailure;
        public Timestamp? MostRecentFailure;
        public int EscalationLevel;
        public Timestamp? DisabledTill;
    }

    [Reducer]
    public static void InsertImportListStatus(ReducerContext ctx, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill)
    {
        RequireAuth(ctx);
        ctx.Db.ImportListStatus.Insert(new ImportListStatus { Id = 0, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill });
    }

    [Reducer]
    public static void UpdateImportListStatus(ReducerContext ctx, int id, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListStatus.Id.Find(id).HasValue, "ImportListStatus", id);
        ctx.Db.ImportListStatus.Id.Update(new ImportListStatus { Id = id, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill });
    }

    [Reducer]
    public static void DeleteImportListStatus(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListStatus.Id.Delete(id), "ImportListStatus", id);
    }

    [Table(Accessor = "NotificationStatus", Public = true)]
    public partial struct NotificationStatus
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int ProviderId;
        public Timestamp? InitialFailure;
        public Timestamp? MostRecentFailure;
        public int EscalationLevel;
        public Timestamp? DisabledTill;
    }

    [Reducer]
    public static void InsertNotificationStatus(ReducerContext ctx, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill)
    {
        RequireAuth(ctx);
        ctx.Db.NotificationStatus.Insert(new NotificationStatus { Id = 0, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill });
    }

    [Reducer]
    public static void UpdateNotificationStatus(ReducerContext ctx, int id, int providerId, Timestamp? initialFailure, Timestamp? mostRecentFailure, int escalationLevel, Timestamp? disabledTill)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.NotificationStatus.Id.Find(id).HasValue, "NotificationStatus", id);
        ctx.Db.NotificationStatus.Id.Update(new NotificationStatus { Id = id, ProviderId = providerId, InitialFailure = initialFailure, MostRecentFailure = mostRecentFailure, EscalationLevel = escalationLevel, DisabledTill = disabledTill });
    }

    [Reducer]
    public static void DeleteNotificationStatus(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.NotificationStatus.Id.Delete(id), "NotificationStatus", id);
    }
}
