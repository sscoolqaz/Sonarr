using SpacetimeDB;

public static partial class Module
{
    // Series/Episodes are LazyLoaded in the SQL model, not persisted directly; Path is Ignore()'d
    // (computed from RelativePath + the series root folder at read time). QualityId is the
    // Phase 3-designed normalized column - always derived from QualityJson by
    // SpacetimeEpisodeFileRepository itself before calling these reducers, never accepted as an
    // independently-trusted value from any other caller (see that repository's remarks).
    [Table(Accessor = "EpisodeFile", Public = true)]
    public partial struct EpisodeFile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int SeriesId;
        public int SeasonNumber;
        public string RelativePath;
        public long Size;
        public Timestamp DateAdded;
        public string OriginalFilePath;
        public string SceneName;
        public string ReleaseGroup;
        public string ReleaseHash;
        public string QualityJson;
        public int QualityId;
        public int IndexerFlags;
        public string MediaInfoJson;
        public string LanguagesJson;
        public int ReleaseType;
    }

    [Reducer]
    public static void InsertEpisodeFile(ReducerContext ctx, int seriesId, int seasonNumber, string relativePath, long size, Timestamp dateAdded, string originalFilePath, string sceneName, string releaseGroup, string releaseHash, string qualityJson, int qualityId, int indexerFlags, string mediaInfoJson, string languagesJson, int releaseType)
    {
        RequireAuth(ctx);
        ctx.Db.EpisodeFile.Insert(new EpisodeFile { Id = 0, SeriesId = seriesId, SeasonNumber = seasonNumber, RelativePath = relativePath, Size = size, DateAdded = dateAdded, OriginalFilePath = originalFilePath, SceneName = sceneName, ReleaseGroup = releaseGroup, ReleaseHash = releaseHash, QualityJson = qualityJson, QualityId = qualityId, IndexerFlags = indexerFlags, MediaInfoJson = mediaInfoJson, LanguagesJson = languagesJson, ReleaseType = releaseType });
    }

    [Reducer]
    public static void UpdateEpisodeFile(ReducerContext ctx, int id, int seriesId, int seasonNumber, string relativePath, long size, Timestamp dateAdded, string originalFilePath, string sceneName, string releaseGroup, string releaseHash, string qualityJson, int qualityId, int indexerFlags, string mediaInfoJson, string languagesJson, int releaseType)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.EpisodeFile.Id.Find(id).HasValue, "EpisodeFile", id);
        ctx.Db.EpisodeFile.Id.Update(new EpisodeFile { Id = id, SeriesId = seriesId, SeasonNumber = seasonNumber, RelativePath = relativePath, Size = size, DateAdded = dateAdded, OriginalFilePath = originalFilePath, SceneName = sceneName, ReleaseGroup = releaseGroup, ReleaseHash = releaseHash, QualityJson = qualityJson, QualityId = qualityId, IndexerFlags = indexerFlags, MediaInfoJson = mediaInfoJson, LanguagesJson = languagesJson, ReleaseType = releaseType });
    }

    [Reducer]
    public static void DeleteEpisodeFile(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.EpisodeFile.Id.Delete(id), "EpisodeFile", id);
    }
}
