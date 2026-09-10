using DryIoc;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Phase 2: opt-in override that swaps the normal SQL-backed ITagRepository for the
    /// SpacetimeDB-backed one, gated behind config so the rest of the app (all other
    /// repositories, still SQLite/Postgres-backed) is unaffected unless explicitly enabled.
    /// This is intentionally narrow - proving the container topology and end-to-end wiring
    /// for one entity, not a real backend switch (that's Phase 3+).
    /// </summary>
    public static class CompositionExtensions
    {
        public static IContainer AddSpacetimeDbTagRepository(this IContainer container, string host, string database)
        {
            container.RegisterDelegate<ITagRepository>(
                _ => new SpacetimeTagRepository(host, database),
                Reuse.Singleton,
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            return container;
        }
    }
}
