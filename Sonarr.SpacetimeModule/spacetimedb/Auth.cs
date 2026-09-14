using System;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB;

public static partial class Module
{
    [Table(Accessor = "ModuleAuthConfig")]
    public partial struct ModuleAuthConfig
    {
        [PrimaryKey]
        public int Id;
        public string Issuer;
        public string ClientIdsCommaSeparated;
    }

    // Public = true + a self-scoped RLS filter (below), not owner-only: confirmed live that an
    // RLS filter on another table (User/Config in Batch1.cs) which JOINs this one is evaluated in
    // the SUBSCRIBING CLIENT's own query context, not the module's - so the client needs its own
    // read access to whatever this JOIN touches for the filter to resolve at all. Without this, a
    // real connected client's own SubscribeToAllTables() failed outright with "no such table:
    // trusted_connection ... it may be marked private" the moment User/Config's own filter tried
    // to reference it, even for a client actually registered in it. The row-level filter below
    // limits what "made Public" actually exposes: a caller can only ever see their OWN row here
    // (never another connection's identity/subject/issuer), which is also exactly what makes the
    // JOIN in User/Config's filters correct - a client's visibility of this table already reduces
    // to "the one row matching my own identity, or nothing," so joining against it naturally
    // produces the intended "all rows visible iff I have a live TrustedConnection entry" result.
    [Table(Accessor = "TrustedConnection", Public = true)]
    public partial struct TrustedConnection
    {
        [PrimaryKey]
        public Identity Identity;
        public string Subject;
        public string Issuer;
        public Timestamp ConnectedAt;
    }

#pragma warning disable STDB_UNSTABLE // ClientVisibilityFilter is an experimental SpacetimeDB feature - the confirmed, correct way to suppress this specific diagnostic, not a general warning suppression.
    [SpacetimeDB.ClientVisibilityFilter]
    public static readonly Filter TrustedConnectionVisibilityFilter = new Filter.Sql(
        "SELECT trusted_connection.* FROM trusted_connection WHERE trusted_connection.identity = :sender"
    );
#pragma warning restore STDB_UNSTABLE

    private static ModuleAuthConfig? GetAuthConfig(ReducerContext ctx)
    {
        return ctx.Db.ModuleAuthConfig.Id.Find(1);
    }

    private static void RequireAuth(ReducerContext ctx)
    {
        var config = GetAuthConfig(ctx);
        if (!config.HasValue)
        {
            return;
        }

        var jwt = ctx.SenderAuth.Jwt;
        if (jwt == null)
        {
            throw new Exception("Authentication required: this module requires a valid OIDC token");
        }

        if (jwt.Issuer != config.Value.Issuer)
        {
            throw new Exception($"Unauthorized: invalid issuer '{jwt.Issuer}'");
        }

        var allowedIds = config.Value.ClientIdsCommaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedIds.Length > 0 && !allowedIds.Any(id => jwt.Audience.Contains(id)))
        {
            throw new Exception("Unauthorized: invalid audience");
        }
    }

    [Reducer(ReducerKind.ClientConnected)]
    public static void ClientConnected(ReducerContext ctx)
    {
        var config = GetAuthConfig(ctx);

        if (!config.HasValue)
        {
            // First-run/migration mode: no OIDC issuer configured yet, so every connection is
            // implicitly trusted (matches ClientConnected's own pre-existing "allow everyone"
            // stance for the WebSocket gate). This ALSO has to register the connection in
            // TrustedConnection, not just skip the auth check - the User/Config RLS filters
            // (Batch1.cs) key off TrustedConnection membership, and would otherwise deny every
            // caller, including this app's own connection, the moment auth is unconfigured.
            ctx.Db.TrustedConnection.Identity.Delete(ctx.Sender);
            ctx.Db.TrustedConnection.Insert(new TrustedConnection
            {
                Identity = ctx.Sender,
                Subject = "unconfigured",
                Issuer = "unconfigured",
                ConnectedAt = ctx.Timestamp
            });

            Log.Info("Auth not configured — allowing unauthenticated connection (first-run/migration mode)");
            return;
        }

        var jwt = ctx.SenderAuth.Jwt;
        if (jwt == null)
        {
            throw new Exception("Authentication required: this module requires a valid OIDC token");
        }

        if (jwt.Issuer != config.Value.Issuer)
        {
            throw new Exception($"Unauthorized: issuer '{jwt.Issuer}' is not trusted");
        }

        var allowedIds = config.Value.ClientIdsCommaSeparated
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedIds.Length > 0 && !allowedIds.Any(id => jwt.Audience.Contains(id)))
        {
            throw new Exception($"Unauthorized: audience not in allowed list");
        }

        ctx.Db.TrustedConnection.Identity.Delete(ctx.Sender);
        ctx.Db.TrustedConnection.Insert(new TrustedConnection
        {
            Identity = ctx.Sender,
            Subject = jwt.Subject,
            Issuer = jwt.Issuer,
            ConnectedAt = ctx.Timestamp
        });

        Log.Info($"Authenticated connection from sub={jwt.Subject}, iss={jwt.Issuer}");
    }

    [Reducer(ReducerKind.ClientDisconnected)]
    public static void ClientDisconnected(ReducerContext ctx)
    {
        ctx.Db.TrustedConnection.Identity.Delete(ctx.Sender);
    }

    [Reducer]
    public static void ConfigureModuleAuth(ReducerContext ctx, string issuer, string clientIdsCommaSeparated)
    {
        var existing = GetAuthConfig(ctx);
        if (existing.HasValue)
        {
            RequireAuth(ctx);
        }

        ctx.Db.ModuleAuthConfig.Id.Delete(1);
        ctx.Db.ModuleAuthConfig.Insert(new ModuleAuthConfig
        {
            Id = 1,
            Issuer = issuer,
            ClientIdsCommaSeparated = clientIdsCommaSeparated
        });

        Log.Info($"Module auth configured: issuer={issuer}, clientIds={clientIdsCommaSeparated}");
    }

    [Reducer]
    public static void ClearModuleAuth(ReducerContext ctx)
    {
        RequireAuth(ctx);
        ctx.Db.ModuleAuthConfig.Id.Delete(1);
        Log.Info("Module auth configuration cleared — all connections now allowed");
    }
}
