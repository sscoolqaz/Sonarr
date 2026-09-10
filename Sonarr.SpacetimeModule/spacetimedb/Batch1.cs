using System.Collections.Generic;
using SpacetimeDB;

public static partial class Module
{
    // --- RemotePathMapping ---
    [Table(Accessor = "RemotePathMapping", Public = true)]
    public partial struct RemotePathMapping
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Host;
        public string RemotePath;
        public string LocalPath;
    }

    [Reducer]
    public static void InsertRemotePathMapping(ReducerContext ctx, string host, string remotePath, string localPath)
    {
        ctx.Db.RemotePathMapping.Insert(new RemotePathMapping { Id = 0, Host = host, RemotePath = remotePath, LocalPath = localPath });
    }

    [Reducer]
    public static void UpdateRemotePathMapping(ReducerContext ctx, int id, string host, string remotePath, string localPath)
    {
        RequireFound(ctx.Db.RemotePathMapping.Id.Find(id).HasValue, "RemotePathMapping", id);
        ctx.Db.RemotePathMapping.Id.Update(new RemotePathMapping { Id = id, Host = host, RemotePath = remotePath, LocalPath = localPath });
    }

    [Reducer]
    public static void DeleteRemotePathMapping(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.RemotePathMapping.Id.Delete(id), "RemotePathMapping", id);
    }

    // --- ImportListExclusion ---
    [Table(Accessor = "ImportListExclusion", Public = true)]
    public partial struct ImportListExclusion
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int TvdbId;
        public string Title;
    }

    [Reducer]
    public static void InsertImportListExclusion(ReducerContext ctx, int tvdbId, string title)
    {
        ctx.Db.ImportListExclusion.Insert(new ImportListExclusion { Id = 0, TvdbId = tvdbId, Title = title });
    }

    [Reducer]
    public static void UpdateImportListExclusion(ReducerContext ctx, int id, int tvdbId, string title)
    {
        RequireFound(ctx.Db.ImportListExclusion.Id.Find(id).HasValue, "ImportListExclusion", id);
        ctx.Db.ImportListExclusion.Id.Update(new ImportListExclusion { Id = id, TvdbId = tvdbId, Title = title });
    }

    [Reducer]
    public static void DeleteImportListExclusion(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.ImportListExclusion.Id.Delete(id), "ImportListExclusion", id);
    }

    // --- CustomFilter ---
    [Table(Accessor = "CustomFilter", Public = true)]
    public partial struct CustomFilter
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Type;
        public string Label;
        public string Filters;
    }

    [Reducer]
    public static void InsertCustomFilter(ReducerContext ctx, string type, string label, string filters)
    {
        ctx.Db.CustomFilter.Insert(new CustomFilter { Id = 0, Type = type, Label = label, Filters = filters });
    }

    [Reducer]
    public static void UpdateCustomFilter(ReducerContext ctx, int id, string type, string label, string filters)
    {
        RequireFound(ctx.Db.CustomFilter.Id.Find(id).HasValue, "CustomFilter", id);
        ctx.Db.CustomFilter.Id.Update(new CustomFilter { Id = id, Type = type, Label = label, Filters = filters });
    }

    [Reducer]
    public static void DeleteCustomFilter(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.CustomFilter.Id.Delete(id), "CustomFilter", id);
    }

    // --- User ---
    [Table(Accessor = "User", Public = true)]
    public partial struct User
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Identifier;
        public string Username;
        public string Password;
        public string Salt;
        public int Iterations;
    }

    [Reducer]
    public static void InsertUser(ReducerContext ctx, string identifier, string username, string password, string salt, int iterations)
    {
        ctx.Db.User.Insert(new User { Id = 0, Identifier = identifier, Username = username, Password = password, Salt = salt, Iterations = iterations });
    }

    [Reducer]
    public static void UpdateUser(ReducerContext ctx, int id, string identifier, string username, string password, string salt, int iterations)
    {
        RequireFound(ctx.Db.User.Id.Find(id).HasValue, "User", id);
        ctx.Db.User.Id.Update(new User { Id = id, Identifier = identifier, Username = username, Password = password, Salt = salt, Iterations = iterations });
    }

    [Reducer]
    public static void DeleteUser(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.User.Id.Delete(id), "User", id);
    }

    // --- Config ---
    [Table(Accessor = "Config", Public = true)]
    public partial struct Config
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Key;
        public string Value;
    }

    [Reducer]
    public static void InsertConfig(ReducerContext ctx, string key, string value)
    {
        ctx.Db.Config.Insert(new Config { Id = 0, Key = key, Value = value });
    }

    [Reducer]
    public static void UpdateConfig(ReducerContext ctx, int id, string key, string value)
    {
        RequireFound(ctx.Db.Config.Id.Find(id).HasValue, "Config", id);
        ctx.Db.Config.Id.Update(new Config { Id = id, Key = key, Value = value });
    }

    [Reducer]
    public static void DeleteConfig(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.Config.Id.Delete(id), "Config", id);
    }

    // --- QualityProfileQualityRank ---
    [Table(Accessor = "QualityProfileQualityRank", Public = true)]
    public partial struct QualityProfileQualityRank
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public int ProfileId;
        public int QualityId;
        public double Score;
    }

    [Reducer]
    public static void InsertQualityProfileQualityRank(ReducerContext ctx, int profileId, int qualityId, double score)
    {
        ctx.Db.QualityProfileQualityRank.Insert(new QualityProfileQualityRank { Id = 0, ProfileId = profileId, QualityId = qualityId, Score = score });
    }

    [Reducer]
    public static void UpdateQualityProfileQualityRank(ReducerContext ctx, int id, int profileId, int qualityId, double score)
    {
        RequireFound(ctx.Db.QualityProfileQualityRank.Id.Find(id).HasValue, "QualityProfileQualityRank", id);
        ctx.Db.QualityProfileQualityRank.Id.Update(new QualityProfileQualityRank { Id = id, ProfileId = profileId, QualityId = qualityId, Score = score });
    }

    [Reducer]
    public static void DeleteQualityProfileQualityRank(ReducerContext ctx, int id)
    {
        RequireFound(ctx.Db.QualityProfileQualityRank.Id.Delete(id), "QualityProfileQualityRank", id);
    }

    // Per the Phase 3 schema design: quality-profile ranks need an atomic replace-whole-set
    // reducer (delete all rows for a profile, insert the replacement set, in one transaction)
    // rather than independent generic delete/insert calls, which would expose a transiently
    // empty or partial rank set to concurrent readers.
    [Reducer]
    public static void ReplaceQualityProfileQualityRanks(ReducerContext ctx, int profileId, List<QualityRankInput> ranks)
    {
        foreach (var existing in ctx.Db.QualityProfileQualityRank.Iter())
        {
            if (existing.ProfileId == profileId)
            {
                ctx.Db.QualityProfileQualityRank.Id.Delete(existing.Id);
            }
        }

        foreach (var rank in ranks)
        {
            ctx.Db.QualityProfileQualityRank.Insert(new QualityProfileQualityRank { Id = 0, ProfileId = profileId, QualityId = rank.QualityId, Score = rank.Score });
        }
    }

    [SpacetimeDB.Type]
    public partial struct QualityRankInput
    {
        public int QualityId;
        public double Score;
    }

    private static void RequireFound(bool found, string entity, int id)
    {
        if (!found)
        {
            throw new System.Exception($"{entity} with id {id} not found");
        }
    }
}
