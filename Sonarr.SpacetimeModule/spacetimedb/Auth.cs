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

    [Table(Accessor = "TrustedConnection")]
    public partial struct TrustedConnection
    {
        [PrimaryKey]
        public Identity Identity;
        public string Subject;
        public string Issuer;
        public Timestamp ConnectedAt;
    }

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
