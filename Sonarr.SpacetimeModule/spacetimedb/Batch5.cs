using SpacetimeDB;

public static partial class Module
{
    // --- ProviderDefinition family (Tier 1.5): IndexerDefinition, ImportListDefinition,
    // NotificationDefinition, DownloadClientDefinition, MetadataDefinition. All five share the
    // exact same persisted shape (Name/Implementation/ConfigContract/Settings/Enable/Tags/Message)
    // - concrete definitions only add Ignore()'d convenience properties on top, per TableMapping.cs.
    [Table(Accessor = "IndexerDefinition", Public = true)]
    public partial struct IndexerDefinition
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string Implementation;
        public string ConfigContract;
        public string SettingsJson;
        public bool Enable;
        public string TagsJson;
        public string MessageJson;
    }

    [Reducer]
    public static void InsertIndexerDefinition(ReducerContext ctx, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        ctx.Db.IndexerDefinition.Insert(new IndexerDefinition { Id = 0, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void UpdateIndexerDefinition(ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.IndexerDefinition.Id.Find(id).HasValue, "IndexerDefinition", id);
        ctx.Db.IndexerDefinition.Id.Update(new IndexerDefinition { Id = id, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void DeleteIndexerDefinition(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.IndexerDefinition.Id.Delete(id), "IndexerDefinition", id);
    }

    [Table(Accessor = "ImportListDefinition", Public = true)]
    public partial struct ImportListDefinition
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string Implementation;
        public string ConfigContract;
        public string SettingsJson;
        public bool Enable;
        public string TagsJson;
        public string MessageJson;
    }

    [Reducer]
    public static void InsertImportListDefinition(ReducerContext ctx, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        ctx.Db.ImportListDefinition.Insert(new ImportListDefinition { Id = 0, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void UpdateImportListDefinition(ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListDefinition.Id.Find(id).HasValue, "ImportListDefinition", id);
        ctx.Db.ImportListDefinition.Id.Update(new ImportListDefinition { Id = id, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void DeleteImportListDefinition(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.ImportListDefinition.Id.Delete(id), "ImportListDefinition", id);
    }

    [Table(Accessor = "NotificationDefinition", Public = true)]
    public partial struct NotificationDefinition
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string Implementation;
        public string ConfigContract;
        public string SettingsJson;
        public bool Enable;
        public string TagsJson;
        public string MessageJson;
    }

    [Reducer]
    public static void InsertNotificationDefinition(ReducerContext ctx, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        ctx.Db.NotificationDefinition.Insert(new NotificationDefinition { Id = 0, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void UpdateNotificationDefinition(ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.NotificationDefinition.Id.Find(id).HasValue, "NotificationDefinition", id);
        ctx.Db.NotificationDefinition.Id.Update(new NotificationDefinition { Id = id, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void DeleteNotificationDefinition(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.NotificationDefinition.Id.Delete(id), "NotificationDefinition", id);
    }

    [Table(Accessor = "DownloadClientDefinition", Public = true)]
    public partial struct DownloadClientDefinition
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string Implementation;
        public string ConfigContract;
        public string SettingsJson;
        public bool Enable;
        public string TagsJson;
        public string MessageJson;
    }

    [Reducer]
    public static void InsertDownloadClientDefinition(ReducerContext ctx, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        ctx.Db.DownloadClientDefinition.Insert(new DownloadClientDefinition { Id = 0, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void UpdateDownloadClientDefinition(ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.DownloadClientDefinition.Id.Find(id).HasValue, "DownloadClientDefinition", id);
        ctx.Db.DownloadClientDefinition.Id.Update(new DownloadClientDefinition { Id = id, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void DeleteDownloadClientDefinition(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.DownloadClientDefinition.Id.Delete(id), "DownloadClientDefinition", id);
    }

    [Table(Accessor = "MetadataDefinition", Public = true)]
    public partial struct MetadataDefinition
    {
        [PrimaryKey, AutoInc]
        public int Id;
        public string Name;
        public string Implementation;
        public string ConfigContract;
        public string SettingsJson;
        public bool Enable;
        public string TagsJson;
        public string MessageJson;
    }

    [Reducer]
    public static void InsertMetadataDefinition(ReducerContext ctx, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        ctx.Db.MetadataDefinition.Insert(new MetadataDefinition { Id = 0, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void UpdateMetadataDefinition(ReducerContext ctx, int id, string name, string implementation, string configContract, string settingsJson, bool enable, string tagsJson, string messageJson)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.MetadataDefinition.Id.Find(id).HasValue, "MetadataDefinition", id);
        ctx.Db.MetadataDefinition.Id.Update(new MetadataDefinition { Id = id, Name = name, Implementation = implementation, ConfigContract = configContract, SettingsJson = settingsJson, Enable = enable, TagsJson = tagsJson, MessageJson = messageJson });
    }

    [Reducer]
    public static void DeleteMetadataDefinition(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        RequireFound(ctx.Db.MetadataDefinition.Id.Delete(id), "MetadataDefinition", id);
    }
}
