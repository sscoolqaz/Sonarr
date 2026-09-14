using SpacetimeDB;

public static partial class Module
{
    // Mirrors NzbDrone.Core.RootFolders.RootFolder. Accessible/IsEmpty/FreeSpace/TotalSpace are
    // computed at read time in the real app (Ignore()'d in TableMapping.cs) - not persisted here
    // either.
    [Table(Accessor = "RootFolder", Public = true)]
    public partial struct RootFolder
    {
        [PrimaryKey, AutoInc]
        public int Id;

        public string Path;
    }

    [Reducer]
    public static void InsertRootFolder(ReducerContext ctx, string path)
    {
        RequireAuth(ctx);
        ctx.Db.RootFolder.Insert(new RootFolder { Id = 0, Path = path });
    }

    [Reducer]
    public static void UpdateRootFolder(ReducerContext ctx, int id, string path)
    {
        RequireAuth(ctx);
        var existing = ctx.Db.RootFolder.Id.Find(id);
        if (!existing.HasValue)
        {
            throw new Exception($"RootFolder with id {id} not found");
        }

        ctx.Db.RootFolder.Id.Update(new RootFolder { Id = id, Path = path });
    }

    [Reducer]
    public static void DeleteRootFolder(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        var deleted = ctx.Db.RootFolder.Id.Delete(id);
        if (!deleted)
        {
            throw new Exception($"RootFolder with id {id} not found");
        }
    }
}
