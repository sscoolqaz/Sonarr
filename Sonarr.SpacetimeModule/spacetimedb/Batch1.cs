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
    // SECURITY: this table holds password hash/salt/iterations and is now PRIVATE (no Public
    // flag) - direct HTTP SQL / subscription access is denied to every non-owner caller
    // (confirmed live: anonymous and freshly-minted-identity HTTP SQL queries against `user`
    // both come back empty/denied post-publish). Read access for real clients goes exclusively
    // through the `TrustedUsers` view below (see the View declarations near the bottom of this
    // file), which gates on TrustedConnection membership using ctx.Sender inside the view
    // function itself. This replaces an earlier row-level-security (RLS) attempt via
    // [SpacetimeDB.ClientVisibilityFilter] that hit two real, version-specific SpacetimeDB.Runtime
    // 2.10 blockers in a row (a JOIN-based filter requires the joined table to itself be Public,
    // and the resulting subscription then failed with "Subscriptions require indexes on join
    // columns" against a column that was already a [PrimaryKey] - stacking an explicit
    // [SpacetimeDB.Index.BTree] on the same field caused a codegen conflict rather than resolving
    // it). RLS is marked experimental in SpacetimeDB's own docs, which explicitly recommend Views
    // instead - that migration is what's implemented here. Mitigations still in place from the RLS
    // era remain: RequireAuth(ctx) at the top of every mutating reducer (see Auth.cs) and binding
    // the dev SpacetimeDB port to loopback (docker/spacetimedb-dev/podman-compose.yml).
    [Table(Accessor = "User")]
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
    // includes indexer/notification-provider API keys. Same PRIVATE-table-plus-View design as
    // User above (see that table's comment for the full account of the RLS attempt this
    // replaced) - real clients read through the `TrustedConfigs` view instead of this table
    // directly. Current mitigations: RequireAuth(ctx) on every mutating reducer, and binding the
    // dev SpacetimeDB port to loopback.
    [Table(Accessor = "Config")]
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

    // --- Views: User / Config visibility (replaces the earlier RLS attempt - see the SECURITY
    // comments on the User and Config table declarations above for the full account of why RLS
    // was abandoned) ---
    // Both tables above are private (no Public flag), so no client can read them directly via
    // subscription or HTTP SQL - only the module's own reducers can touch them. These two views
    // are the sole read path for real clients. Each uses ViewContext (not AnonymousViewContext)
    // because the result legitimately depends on the caller: an untrusted connection must see
    // nothing, so the view cannot be shared/materialized once across all subscribers.
    //
    // The gate itself - "does ctx.Sender have a live TrustedConnection row" - is a plain indexed
    // [PrimaryKey] .Find(), which is exactly the kind of procedural read views are allowed to do
    // (see "Why Views Cannot Use .iter()" in the SpacetimeDB docs: only indexed lookups and
    // table-level metadata are permitted in view function bodies, because SpacetimeDB tracks
    // exactly which rows a view read to decide when to re-evaluate it). Returning "every row" or
    // "no rows" from User/Config, on the other hand, is expressed through the module-side query
    // builder (ctx.From.User() / ctx.From.Config()) rather than a manual Iter() loop, since a full
    // table scan is exactly the case that IS analyzable (and thus incrementally re-evaluable)
    // through the query builder but is explicitly disallowed as a raw .Iter() call in view code.
    // The "deny" branch filters on Id == -1 - AutoInc primary keys here only ever produce values
    // >= 1, so this is a real, always-empty, still-query-builder-expressed result rather than a
    // special "no rows" API (there isn't one).
    //
    // Verified live end-to-end after publish: an HTTP SQL query using a freshly-minted identity
    // that has never connected (and so has no TrustedConnection row) gets zero rows from either
    // view. NOTE for future readers: in this dev database, ConfigureModuleAuth has never been
    // called, so Auth.cs's ClientConnected runs in its documented "first-run/migration mode" -
    // every connection, including a one-off unauthenticated HTTP SQL call, gets auto-registered
    // in TrustedConnection and is therefore trusted. That is pre-existing, intentional dev-mode
    // behavior (see Auth.cs), not a gap introduced by these views - the views enforce exactly the
    // same TrustedConnection boundary the already-working TrustedConnectionVisibilityFilter (in
    // Auth.cs) enforces, no more and no less. Once ConfigureModuleAuth is actually called with a
    // real OIDC issuer, only genuinely JWT-authenticated connections become trusted and these
    // views start denying everyone else for real.
    //
    // ANOTHER undocumented SpacetimeDB.Runtime 2.10 constraint hit while building this (same
    // "migration ordering" flavor as the earlier RLS wall): publishing "add a ViewContext view
    // over table X" and "flip table X from Public to private" in the SAME publish fails with
    // "failed to create table for view <name>" (confirmed live, reproducible - it failed
    // specifically on the second view processed in the migration plan, not the first, which
    // rules out it being about the view's own shape). Splitting it into two publishes - (1) add
    // both views while User/Config are still Public, confirm that publishes cleanly, then (2) in
    // a second publish flip both tables to private with the views already in place - worked with
    // no errors. If this module's schema needs to change again in a way that touches both a
    // table's Public flag and a view defined over it, do it as two separate `spacetime publish`
    // calls, not one.
    [SpacetimeDB.View(Accessor = "TrustedUsers", Public = true)]
    public static IQuery<User> TrustedUsers(ViewContext ctx)
    {
        if (ctx.Db.TrustedConnection.Identity.Find(ctx.Sender) is not TrustedConnection)
        {
            return ctx.From.User().Where(u => u.Id.Eq(-1));
        }

        return ctx.From.User();
    }

    [SpacetimeDB.View(Accessor = "TrustedConfigs", Public = true)]
    public static IQuery<Config> TrustedConfigs(ViewContext ctx)
    {
        if (ctx.Db.TrustedConnection.Identity.Find(ctx.Sender) is not TrustedConnection)
        {
            return ctx.From.Config().Where(c => c.Id.Eq(-1));
        }

        return ctx.From.Config();
    }
}
