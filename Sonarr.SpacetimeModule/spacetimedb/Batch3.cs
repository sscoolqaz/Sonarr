using SpacetimeDB;

public static partial class Module
{
    // --- DownloadHistory ---
    [Table(Accessor = "DownloadHistory", Public = true)]
    public partial struct DownloadHistory
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int EventType;
        public int SeriesId;
        public string DownloadId;
        public string SourceTitle;
        public Timestamp Date;
        public int Protocol;
        public int IndexerId;
        public int DownloadClientId;
        public string ReleaseJson;
        public string DataJson;
    }

    [Reducer]
    public static void InsertDownloadHistory(ReducerContext ctx, int eventType, int seriesId, string downloadId, string sourceTitle, Timestamp date, int protocol, int indexerId, int downloadClientId, string releaseJson, string dataJson)
    {
        RequireAuth(ctx);
        ctx.Db.DownloadHistory.Insert(new DownloadHistory { Id = 0, EventType = eventType, SeriesId = seriesId, DownloadId = downloadId, SourceTitle = sourceTitle, Date = date, Protocol = protocol, IndexerId = indexerId, DownloadClientId = downloadClientId, ReleaseJson = releaseJson, DataJson = dataJson });
    }

    [Reducer]
    public static void UpdateDownloadHistory(ReducerContext ctx, int id, int eventType, int seriesId, string downloadId, string sourceTitle, Timestamp date, int protocol, int indexerId, int downloadClientId, string releaseJson, string dataJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.DownloadHistory.Id.Find(id).HasValue, "DownloadHistory", id);
        ctx.Db.DownloadHistory.Id.Update(new DownloadHistory { Id = id, EventType = eventType, SeriesId = seriesId, DownloadId = downloadId, SourceTitle = sourceTitle, Date = date, Protocol = protocol, IndexerId = indexerId, DownloadClientId = downloadClientId, ReleaseJson = releaseJson, DataJson = dataJson });
    }

    [Reducer]
    public static void DeleteDownloadHistory(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.DownloadHistory.Id.Delete(id), "DownloadHistory", id);
    }

    // --- CustomFormat ---
    [Table(Accessor = "CustomFormat", Public = true)]
    public partial struct CustomFormat
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public bool IncludeCustomFormatWhenRenaming;
        public string SpecificationsJson;
    }

    [Reducer]
    public static void InsertCustomFormat(ReducerContext ctx, string name, bool includeCustomFormatWhenRenaming, string specificationsJson)
    {
        RequireAuth(ctx);
        ctx.Db.CustomFormat.Insert(new CustomFormat { Id = 0, Name = name, IncludeCustomFormatWhenRenaming = includeCustomFormatWhenRenaming, SpecificationsJson = specificationsJson });
    }

    [Reducer]
    public static void UpdateCustomFormat(ReducerContext ctx, int id, string name, bool includeCustomFormatWhenRenaming, string specificationsJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.CustomFormat.Id.Find(id).HasValue, "CustomFormat", id);
        ctx.Db.CustomFormat.Id.Update(new CustomFormat { Id = id, Name = name, IncludeCustomFormatWhenRenaming = includeCustomFormatWhenRenaming, SpecificationsJson = specificationsJson });
    }

    [Reducer]
    public static void DeleteCustomFormat(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.CustomFormat.Id.Delete(id), "CustomFormat", id);
    }

    // --- AutoTag ---
    [Table(Accessor = "AutoTag", Public = true)]
    public partial struct AutoTag
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string SpecificationsJson;
        public bool RemoveTagsAutomatically;
        public string TagsJson;
    }

    [Reducer]
    public static void InsertAutoTag(ReducerContext ctx, string name, string specificationsJson, bool removeTagsAutomatically, string tagsJson)
    {
        RequireAuth(ctx);
        ctx.Db.AutoTag.Insert(new AutoTag { Id = 0, Name = name, SpecificationsJson = specificationsJson, RemoveTagsAutomatically = removeTagsAutomatically, TagsJson = tagsJson });
    }

    [Reducer]
    public static void UpdateAutoTag(ReducerContext ctx, int id, string name, string specificationsJson, bool removeTagsAutomatically, string tagsJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.AutoTag.Id.Find(id).HasValue, "AutoTag", id);
        ctx.Db.AutoTag.Id.Update(new AutoTag { Id = id, Name = name, SpecificationsJson = specificationsJson, RemoveTagsAutomatically = removeTagsAutomatically, TagsJson = tagsJson });
    }

    [Reducer]
    public static void DeleteAutoTag(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.AutoTag.Id.Delete(id), "AutoTag", id);
    }

    // --- ImportListItem ---
    [Table(Accessor = "ImportListItem", Public = true)]
    public partial struct ImportListItem
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int ImportListId;
        public string Title;
        public int Year;
        public int TvdbId;
        public int TmdbId;
        public string ImdbId;
        public int MalId;
        public int AniListId;
        public Timestamp ReleaseDate;
    }

    [Reducer]
    public static void InsertImportListItem(ReducerContext ctx, int importListId, string title, int year, int tvdbId, int tmdbId, string imdbId, int malId, int aniListId, Timestamp releaseDate)
    {
        RequireAuth(ctx);
        ctx.Db.ImportListItem.Insert(new ImportListItem { Id = 0, ImportListId = importListId, Title = title, Year = year, TvdbId = tvdbId, TmdbId = tmdbId, ImdbId = imdbId, MalId = malId, AniListId = aniListId, ReleaseDate = releaseDate });
    }

    [Reducer]
    public static void UpdateImportListItem(ReducerContext ctx, int id, int importListId, string title, int year, int tvdbId, int tmdbId, string imdbId, int malId, int aniListId, Timestamp releaseDate)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListItem.Id.Find(id).HasValue, "ImportListItem", id);
        ctx.Db.ImportListItem.Id.Update(new ImportListItem { Id = id, ImportListId = importListId, Title = title, Year = year, TvdbId = tvdbId, TmdbId = tmdbId, ImdbId = imdbId, MalId = malId, AniListId = aniListId, ReleaseDate = releaseDate });
    }

    [Reducer]
    public static void DeleteImportListItem(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListItem.Id.Delete(id), "ImportListItem", id);
    }

    // --- Command ---
    [Table(Accessor = "Command", Public = true)]
    public partial struct CommandRow
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string BodyJson;
        public int Priority;
        public int Status;
        public int Result;
        public Timestamp QueuedAt;
        public Timestamp? StartedAt;
        public Timestamp? EndedAt;
        public long? DurationTicks;
        public string Exception;
        public int Trigger;
    }

    [Reducer]
    public static void InsertCommand(ReducerContext ctx, string name, string bodyJson, int priority, int status, int result, Timestamp queuedAt, Timestamp? startedAt, Timestamp? endedAt, long? durationTicks, string exception, int trigger)
    {
        RequireAuth(ctx);
        ctx.Db.Command.Insert(new CommandRow { Id = 0, Name = name, BodyJson = bodyJson, Priority = priority, Status = status, Result = result, QueuedAt = queuedAt, StartedAt = startedAt, EndedAt = endedAt, DurationTicks = durationTicks, Exception = exception, Trigger = trigger });
    }

    [Reducer]
    public static void UpdateCommand(ReducerContext ctx, int id, string name, string bodyJson, int priority, int status, int result, Timestamp queuedAt, Timestamp? startedAt, Timestamp? endedAt, long? durationTicks, string exception, int trigger)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Command.Id.Find(id).HasValue, "Command", id);
        ctx.Db.Command.Id.Update(new CommandRow { Id = id, Name = name, BodyJson = bodyJson, Priority = priority, Status = status, Result = result, QueuedAt = queuedAt, StartedAt = startedAt, EndedAt = endedAt, DurationTicks = durationTicks, Exception = exception, Trigger = trigger });
    }

    [Reducer]
    public static void DeleteCommand(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Command.Id.Delete(id), "Command", id);
    }

    [Reducer]
    public static void OrphanStartedCommands(ReducerContext ctx, int orphanedStatus, int startedStatus, Timestamp endedAt)
    {
        RequireAuth(ctx);
        foreach (var row in ctx.Db.Command.Iter())
        {
            if (row.Status == startedStatus)
            {
                var updated = row;
                updated.Status = orphanedStatus;
                updated.EndedAt = endedAt;
                ctx.Db.Command.Id.Update(updated);
            }
        }
    }

    // --- PendingRelease ---
    [Table(Accessor = "PendingRelease", Public = true)]
    public partial struct PendingRelease
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public string Title;
        public Timestamp Added;
        public string ParsedEpisodeInfoJson;
        public string ReleaseJson;
        public int Reason;
        public string AdditionalInfoJson;
    }

    [Reducer]
    public static void InsertPendingRelease(ReducerContext ctx, int seriesId, string title, Timestamp added, string parsedEpisodeInfoJson, string releaseJson, int reason, string additionalInfoJson)
    {
        RequireAuth(ctx);
        ctx.Db.PendingRelease.Insert(new PendingRelease { Id = 0, SeriesId = seriesId, Title = title, Added = added, ParsedEpisodeInfoJson = parsedEpisodeInfoJson, ReleaseJson = releaseJson, Reason = reason, AdditionalInfoJson = additionalInfoJson });
    }

    [Reducer]
    public static void UpdatePendingRelease(ReducerContext ctx, int id, int seriesId, string title, Timestamp added, string parsedEpisodeInfoJson, string releaseJson, int reason, string additionalInfoJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.PendingRelease.Id.Find(id).HasValue, "PendingRelease", id);
        ctx.Db.PendingRelease.Id.Update(new PendingRelease { Id = id, SeriesId = seriesId, Title = title, Added = added, ParsedEpisodeInfoJson = parsedEpisodeInfoJson, ReleaseJson = releaseJson, Reason = reason, AdditionalInfoJson = additionalInfoJson });
    }

    [Reducer]
    public static void DeletePendingRelease(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.PendingRelease.Id.Delete(id), "PendingRelease", id);
    }

    // --- UpdateHistory --- (ported as an ordinary table per the Phase 3 v2 correction - not
    // dropped alongside LogDatabase/Log, since its loss would be a real feature regression)
    [Table(Accessor = "UpdateHistory", Public = true)]
    public partial struct UpdateHistory
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public Timestamp Date;
        public string Version;
        public int EventType;
    }

    [Reducer]
    public static void InsertUpdateHistory(ReducerContext ctx, Timestamp date, string version, int eventType)
    {
        RequireAuth(ctx);
        ctx.Db.UpdateHistory.Insert(new UpdateHistory { Id = 0, Date = date, Version = version, EventType = eventType });
    }

    [Reducer]
    public static void UpdateUpdateHistory(ReducerContext ctx, int id, Timestamp date, string version, int eventType)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.UpdateHistory.Id.Find(id).HasValue, "UpdateHistory", id);
        ctx.Db.UpdateHistory.Id.Update(new UpdateHistory { Id = id, Date = date, Version = version, EventType = eventType });
    }

    [Reducer]
    public static void DeleteUpdateHistory(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.UpdateHistory.Id.Delete(id), "UpdateHistory", id);
    }
}
