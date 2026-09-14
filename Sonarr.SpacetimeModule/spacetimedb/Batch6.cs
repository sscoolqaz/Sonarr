using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    // --- QualityProfile (Tier 1.5): no SQL joins in the real repository, everything is a
    // single-table read plus a client-side CustomFormat rehydration step - see
    // SpacetimeQualityProfileRepository for the rehydration logic mirroring
    // QualityProfileRepository.Query()'s override exactly.
    [Table(Accessor = "QualityProfile", Public = true)]
    public partial struct QualityProfile
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public bool UpgradeAllowed;
        public int Cutoff;
        public int MinFormatScore;
        public int CutoffFormatScore;
        public int MinUpgradeFormatScore;
        public string FormatItemsJson;
        public string ItemsJson;
    }

    [Reducer]
    public static void InsertQualityProfile(ReducerContext ctx, string name, bool upgradeAllowed, int cutoff, int minFormatScore, int cutoffFormatScore, int minUpgradeFormatScore, string formatItemsJson, string itemsJson)
    {
        RequireAuth(ctx);
        ctx.Db.QualityProfile.Insert(new QualityProfile { Id = 0, Name = name, UpgradeAllowed = upgradeAllowed, Cutoff = cutoff, MinFormatScore = minFormatScore, CutoffFormatScore = cutoffFormatScore, MinUpgradeFormatScore = minUpgradeFormatScore, FormatItemsJson = formatItemsJson, ItemsJson = itemsJson });
    }

    [Reducer]
    public static void UpdateQualityProfile(ReducerContext ctx, int id, string name, bool upgradeAllowed, int cutoff, int minFormatScore, int cutoffFormatScore, int minUpgradeFormatScore, string formatItemsJson, string itemsJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.QualityProfile.Id.Find(id).HasValue, "QualityProfile", id);
        ctx.Db.QualityProfile.Id.Update(new QualityProfile { Id = id, Name = name, UpgradeAllowed = upgradeAllowed, Cutoff = cutoff, MinFormatScore = minFormatScore, CutoffFormatScore = cutoffFormatScore, MinUpgradeFormatScore = minUpgradeFormatScore, FormatItemsJson = formatItemsJson, ItemsJson = itemsJson });
    }

    [Reducer]
    public static void DeleteQualityProfile(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.QualityProfile.Id.Delete(id), "QualityProfile", id);
    }

    // --- Series (Tier 1.5). RootFolderPath is Ignore()'d in the real TableMapping (computed
    // post-load, not persisted) so it has no column here either. QualityProfile itself is
    // LazyLoaded (only QualityProfileId is a real FK) - same convention as EpisodeFile's
    // LazyLoaded Series/Episodes, left unpopulated by ToModel. Tags is deliberately NOT an
    // embedded column here, unlike the real SQL schema - see SeriesTag below.
    [Table(Accessor = "Series", Public = true)]
    public partial struct Series
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int TvdbId;
        public int TvRageId;
        public int TvMazeId;
        public string ImdbId;
        public int TmdbId;
        public string MalIdsJson;
        public string AniListIdsJson;
        public string Title;
        public string CleanTitle;
        public string SortTitle;
        public int Status;
        public string Overview;
        public string AirTime;
        public bool Monitored;
        public int MonitorNewItems;
        public int QualityProfileId;
        public bool SeasonFolder;
        public Timestamp? LastInfoSync;
        public int Runtime;
        public string ImagesJson;
        public int SeriesType;
        public string Network;
        public bool UseSceneNumbering;
        public string TitleSlug;
        public string Path;
        public int Year;
        public string RatingsJson;
        public string GenresJson;
        public string ActorsJson;
        public string Certification;
        public Timestamp Added;
        public Timestamp? FirstAired;
        public Timestamp? LastAired;
        public string OriginalLanguageJson;
        public string OriginalCountry;
        public string SeasonsJson;
        public string AddOptionsJson;
    }

    // Both reducers below take tagIds and replace the series' SeriesTag rows as part of the SAME
    // transaction as the Series row write (via ReplaceSeriesTagsInternal) - per the Phase 4
    // adversarial review, writing them as two separate reducer calls (the port's original design)
    // meant a reader could observe a series with no tags yet, and a failure between the two calls
    // could leave them permanently out of sync. ReplaceSeriesTags itself is kept as a standalone
    // reducer too (still used by Sonarr.SpacetimeMigration, which inserts a series and replaces
    // its tags as two separate migration-tool calls with its own row-count-based confirmation).
    [Reducer]
    public static void InsertSeries(
        ReducerContext ctx,
        int tvdbId, int tvRageId, int tvMazeId, string imdbId, int tmdbId, string malIdsJson, string aniListIdsJson,
        string title, string cleanTitle, string sortTitle, int status, string overview, string airTime,
        bool monitored, int monitorNewItems, int qualityProfileId, bool seasonFolder, Timestamp? lastInfoSync,
        int runtime, string imagesJson, int seriesType, string network, bool useSceneNumbering, string titleSlug,
        string path, int year, string ratingsJson, string genresJson, string actorsJson, string certification,
        Timestamp added, Timestamp? firstAired, Timestamp? lastAired, string originalLanguageJson,
        string originalCountry, string seasonsJson, string addOptionsJson, List<int> tagIds)
    {
        RequireAuth(ctx);
        var inserted = ctx.Db.Series.Insert(new Series
        {
            Id = 0, TvdbId = tvdbId, TvRageId = tvRageId, TvMazeId = tvMazeId, ImdbId = imdbId, TmdbId = tmdbId,
            MalIdsJson = malIdsJson, AniListIdsJson = aniListIdsJson, Title = title, CleanTitle = cleanTitle,
            SortTitle = sortTitle, Status = status, Overview = overview, AirTime = airTime, Monitored = monitored,
            MonitorNewItems = monitorNewItems, QualityProfileId = qualityProfileId, SeasonFolder = seasonFolder,
            LastInfoSync = lastInfoSync, Runtime = runtime, ImagesJson = imagesJson, SeriesType = seriesType,
            Network = network, UseSceneNumbering = useSceneNumbering, TitleSlug = titleSlug, Path = path, Year = year,
            RatingsJson = ratingsJson, GenresJson = genresJson, ActorsJson = actorsJson, Certification = certification,
            Added = added, FirstAired = firstAired, LastAired = lastAired, OriginalLanguageJson = originalLanguageJson,
            OriginalCountry = originalCountry, SeasonsJson = seasonsJson, AddOptionsJson = addOptionsJson
        });

        ReplaceSeriesTagsInternal(ctx, inserted.Id, tagIds);
    }

    [Reducer]
    public static void UpdateSeries(
        ReducerContext ctx,
        int id, int tvdbId, int tvRageId, int tvMazeId, string imdbId, int tmdbId, string malIdsJson, string aniListIdsJson,
        string title, string cleanTitle, string sortTitle, int status, string overview, string airTime,
        bool monitored, int monitorNewItems, int qualityProfileId, bool seasonFolder, Timestamp? lastInfoSync,
        int runtime, string imagesJson, int seriesType, string network, bool useSceneNumbering, string titleSlug,
        string path, int year, string ratingsJson, string genresJson, string actorsJson, string certification,
        Timestamp added, Timestamp? firstAired, Timestamp? lastAired, string originalLanguageJson,
        string originalCountry, string seasonsJson, string addOptionsJson, List<int> tagIds)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Series.Id.Find(id).HasValue, "Series", id);
        ctx.Db.Series.Id.Update(new Series
        {
            Id = id, TvdbId = tvdbId, TvRageId = tvRageId, TvMazeId = tvMazeId, ImdbId = imdbId, TmdbId = tmdbId,
            MalIdsJson = malIdsJson, AniListIdsJson = aniListIdsJson, Title = title, CleanTitle = cleanTitle,
            SortTitle = sortTitle, Status = status, Overview = overview, AirTime = airTime, Monitored = monitored,
            MonitorNewItems = monitorNewItems, QualityProfileId = qualityProfileId, SeasonFolder = seasonFolder,
            LastInfoSync = lastInfoSync, Runtime = runtime, ImagesJson = imagesJson, SeriesType = seriesType,
            Network = network, UseSceneNumbering = useSceneNumbering, TitleSlug = titleSlug, Path = path, Year = year,
            RatingsJson = ratingsJson, GenresJson = genresJson, ActorsJson = actorsJson, Certification = certification,
            Added = added, FirstAired = firstAired, LastAired = lastAired, OriginalLanguageJson = originalLanguageJson,
            OriginalCountry = originalCountry, SeasonsJson = seasonsJson, AddOptionsJson = addOptionsJson
        });

        ReplaceSeriesTagsInternal(ctx, id, tagIds);
    }

    [Reducer]
    public static void DeleteSeries(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Series.Id.Delete(id), "Series", id);
    }

    // --- SeriesTag junction table. Per the Phase 3 schema design review: the real SQL schema
    // stores Series.Tags as an embedded HashSet<int> column, but SpacetimeDB's WHERE grammar has
    // no array-contains operator, so an embedded array can't be filtered by tag membership at
    // all. Replaced with a normal junction table plus an atomic replace-whole-set reducer (same
    // pattern as ReplaceQualityProfileQualityRanks above) so tag writes - which happen via at
    // least three different call shapes in the real SeriesService/SeriesEditorController - never
    // expose a transiently empty or partially-replaced tag set to a concurrent reader.
    //
    // SECURITY/PERF: SpacetimeSeriesRepository.TagIdsFor (client-side, not in this module) was
    // doing Iter().Where(t => t.SeriesId == seriesId) - a full table scan per series. C#
    // SpacetimeDB.Runtime 2.10 DOES support declaring a secondary non-unique B-tree index via
    // the field-level [SpacetimeDB.Index.BTree] attribute (confirmed against the official
    // tables/indexes docs - earlier reflection against TableAttribute's own properties missed
    // this because the index is declared on the COLUMN, not the table attribute). SeriesId below
    // now carries that index, generating a ctx.Db.SeriesTag.SeriesId accessor with .Filter(id)
    // for an indexed equality lookup - the client-side repository should switch to it instead of
    // Iter().Where(...) as a follow-up (out of scope here: SpacetimeSeriesRepository.cs is
    // src/NzbDrone.Core/Datastore/SpacetimeDb/, owned by the parallel client-side agent).
    [Table(Accessor = "SeriesTag", Public = true)]
    public partial struct SeriesTag
    {
        [PrimaryKey, AutoInc]
        public int Id;

        [SpacetimeDB.Index.BTree]
        public int SeriesId;

        public int TagId;
    }

    [Reducer]
    public static void ReplaceSeriesTags(ReducerContext ctx, int seriesId, List<int> tagIds)
    {
        RequireAuth(ctx);
        ReplaceSeriesTagsInternal(ctx, seriesId, tagIds);
    }

    private static void ReplaceSeriesTagsInternal(ReducerContext ctx, int seriesId, List<int> tagIds)
    {
        foreach (var existing in ctx.Db.SeriesTag.Iter())
        {
            if (existing.SeriesId == seriesId)
            {
                ctx.Db.SeriesTag.Id.Delete(existing.Id);
            }
        }

        foreach (var tagId in tagIds)
        {
            ctx.Db.SeriesTag.Insert(new SeriesTag { Id = 0, SeriesId = seriesId, TagId = tagId });
        }
    }
}
