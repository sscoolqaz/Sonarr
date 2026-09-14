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
        RequireAuth(ctx);
        ctx.Db.RemotePathMapping.Insert(new RemotePathMapping { Id = 0, Host = host, RemotePath = remotePath, LocalPath = localPath });
    }

    [Reducer]
    public static void UpdateRemotePathMapping(ReducerContext ctx, int id, string host, string remotePath, string localPath)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.RemotePathMapping.Id.Find(id).HasValue, "RemotePathMapping", id);
        ctx.Db.RemotePathMapping.Id.Update(new RemotePathMapping { Id = id, Host = host, RemotePath = remotePath, LocalPath = localPath });
    }

    [Reducer]
    public static void DeleteRemotePathMapping(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
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
        RequireAuth(ctx);
        ctx.Db.ImportListExclusion.Insert(new ImportListExclusion { Id = 0, TvdbId = tvdbId, Title = title });
    }

    [Reducer]
    public static void UpdateImportListExclusion(ReducerContext ctx, int id, int tvdbId, string title)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListExclusion.Id.Find(id).HasValue, "ImportListExclusion", id);
        ctx.Db.ImportListExclusion.Id.Update(new ImportListExclusion { Id = id, TvdbId = tvdbId, Title = title });
    }

    [Reducer]
    public static void DeleteImportListExclusion(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
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
        RequireAuth(ctx);
        ctx.Db.CustomFilter.Insert(new CustomFilter { Id = 0, Type = type, Label = label, Filters = filters });
    }

    [Reducer]
    public static void UpdateCustomFilter(ReducerContext ctx, int id, string type, string label, string filters)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.CustomFilter.Id.Find(id).HasValue, "CustomFilter", id);
        ctx.Db.CustomFilter.Id.Update(new CustomFilter { Id = id, Type = type, Label = label, Filters = filters });
    }

    [Reducer]
    public static void DeleteCustomFilter(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.CustomFilter.Id.Delete(id), "CustomFilter", id);
    }

    // --- User ---
    // SECURITY: this table holds password hash/salt/iterations, and is Public - this session
    // attempted to restrict it with a row-level-security filter joining against
    // TrustedConnection and could not get it working: SpacetimeDB requires a table to be Public
    // before an RLS rule can apply to it at all (confirmed live: a non-Public table with the
    // filter attached fails to publish with "Cannot define RLS rule on private table"); making
    // TrustedConnection itself Public+self-filtered to satisfy that in turn hit a second,
    // apparently undocumented constraint - the subscription this filter implies failed with
    // "Subscriptions require indexes on join columns" even though the joined column
    // (TrustedConnection.Identity) is already a [PrimaryKey] (stacking an explicit
    // [SpacetimeDB.Index.BTree] on the same field produced a codegen conflict with the
    // auto-generated PrimaryKey index instead of resolving it). SpacetimeDB.Runtime 2.10's own
    // RLS feature is marked experimental and the official docs recommend Views over RLS for new
    // access-control designs - given two real, version-specific blockers in a row, that migration
    // (a View exposing only non-secret User metadata, with credential verification staying in a
    // reducer) is the better next step, not further RLS troubleshooting. Until then, the
    // mitigations in place are: RequireAuth(ctx) at the top of every mutating reducer in this
    // module (closes the HTTP reducer-call bypass - see Auth.cs) and binding the dev SpacetimeDB
    // port to loopback (docker/spacetimedb-dev/podman-compose.yml). Table-level read access
    // (HTTP SQL, subscriptions) is NOT currently restricted beyond that network boundary.
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
        RequireAuth(ctx);
        ctx.Db.User.Insert(new User { Id = 0, Identifier = identifier, Username = username, Password = password, Salt = salt, Iterations = iterations });
    }

    [Reducer]
    public static void UpdateUser(ReducerContext ctx, int id, string identifier, string username, string password, string salt, int iterations)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.User.Id.Find(id).HasValue, "User", id);
        ctx.Db.User.Id.Update(new User { Id = id, Identifier = identifier, Username = username, Password = password, Salt = salt, Iterations = iterations });
    }

    [Reducer]
    public static void DeleteUser(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.User.Id.Delete(id), "User", id);
    }

    // --- Config ---
    // SECURITY: this table holds arbitrary key/value settings - in real Sonarr usage this
    // includes indexer/notification-provider API keys. Same unresolved RLS situation as User
    // above - see that table's comment for the full account of what was tried and why it's
    // deferred to a View-based redesign instead. Current mitigations: RequireAuth(ctx) on every
    // mutating reducer, and binding the dev SpacetimeDB port to loopback.
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
        RequireAuth(ctx);
        ctx.Db.Config.Insert(new Config { Id = 0, Key = key, Value = value });
    }

    [Reducer]
    public static void UpdateConfig(ReducerContext ctx, int id, string key, string value)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.Config.Id.Find(id).HasValue, "Config", id);
        ctx.Db.Config.Id.Update(new Config { Id = id, Key = key, Value = value });
    }

    [Reducer]
    public static void DeleteConfig(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
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
        RequireAuth(ctx);
        ctx.Db.QualityProfileQualityRank.Insert(new QualityProfileQualityRank { Id = 0, ProfileId = profileId, QualityId = qualityId, Score = score });
    }

    [Reducer]
    public static void UpdateQualityProfileQualityRank(ReducerContext ctx, int id, int profileId, int qualityId, double score)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.QualityProfileQualityRank.Id.Find(id).HasValue, "QualityProfileQualityRank", id);
        ctx.Db.QualityProfileQualityRank.Id.Update(new QualityProfileQualityRank { Id = id, ProfileId = profileId, QualityId = qualityId, Score = score });
    }

    [Reducer]
    public static void DeleteQualityProfileQualityRank(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.QualityProfileQualityRank.Id.Delete(id), "QualityProfileQualityRank", id);
    }

    // Per the Phase 3 schema design: quality-profile ranks need an atomic replace-whole-set
    // reducer (delete all rows for a profile, insert the replacement set, in one transaction)
    // rather than independent generic delete/insert calls, which would expose a transiently
    // empty or partial rank set to concurrent readers.
    [Reducer]
    public static void ReplaceQualityProfileQualityRanks(ReducerContext ctx, int profileId, List<QualityRankInput> ranks)
    {
        RequireAuth(ctx);
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

    // --- Row-level security: User / Config (attempted, reverted - see the SECURITY comments on
    // the User and Config table declarations above for the full account) ---
    // A JOIN-based RLS filter here ("SELECT user.* FROM user JOIN trusted_connection WHERE
    // trusted_connection.identity = :sender") got as far as publishing successfully, but the
    // resulting subscription failed at runtime with "Subscriptions require indexes on join
    // columns" against TrustedConnection.Identity despite it already being a [PrimaryKey] -
    // an apparently undocumented SpacetimeDB.Runtime 2.10 constraint, not a mistake in this
    // filter's shape (it matches the official how-to-rls docs' own admin-filter example
    // verbatim). Left removed rather than publishing something broken; do not re-add without
    // first resolving that constraint or moving to a View-based design instead (SpacetimeDB's
    // own docs recommend Views over RLS for new access-control work).
}
