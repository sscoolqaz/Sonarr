using SpacetimeDB;

public static partial class Module
{
    // Mirrors NzbDrone.Core.Tags.Tag (Id : ModelBase.Id, Label : string)
    [Table(Accessor = "Tag", Public = true)]
    public partial struct Tag
    {
        [PrimaryKey, AutoInc]
        public int Id;

        public string Label;
    }

    // Mirrors BasicRepository<Tag>.Insert: caller supplies Id == 0, server assigns it.
    [Reducer]
    public static void InsertTag(ReducerContext ctx, string label)
    {
        RequireAuth(ctx);
        ctx.Db.Tag.Insert(new Tag { Id = 0, Label = label });
    }

    // Mirrors BasicRepository<Tag>.Update: full-row update keyed by Id.
    [Reducer]
    public static void UpdateTag(ReducerContext ctx, int id, string label)
    {
        RequireAuth(ctx);
        var existing = ctx.Db.Tag.Id.Find(id);
        if (!existing.HasValue)
        {
            throw new Exception($"Tag with id {id} not found");
        }

        ctx.Db.Tag.Id.Update(new Tag { Id = id, Label = label });
    }

    // Mirrors BasicRepository<Tag>.Delete(int id).
    [Reducer]
    public static void DeleteTag(ReducerContext ctx, int id)
    {
        RequireAuth(ctx);
        var deleted = ctx.Db.Tag.Id.Delete(id);
        if (!deleted)
        {
            throw new Exception($"Tag with id {id} not found");
        }
    }
}
