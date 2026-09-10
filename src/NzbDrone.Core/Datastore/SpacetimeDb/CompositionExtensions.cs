using DryIoc;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Opt-in override that swaps a growing set of SQL-backed repositories for their
    /// SpacetimeDB-backed equivalents, gated behind config so the rest of the app is unaffected
    /// unless explicitly enabled. Phase 4 adds one entity at a time to AddSpacetimeDbRepositories
    /// as each is ported - everything not yet listed here still uses the normal SQLite path.
    /// </summary>
    public static class CompositionExtensions
    {
        public static IContainer AddSpacetimeDbRepositories(this IContainer container, string host, string database)
        {
            container.RegisterInstance<ISpacetimeDbConnection>(
                new SpacetimeDbConnection(host, database),
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            container.Register<ITagRepository, SpacetimeTagRepository>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.Register<IRootFolderRepository, SpacetimeRootFolderRepository>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            return container;
        }
    }
}
