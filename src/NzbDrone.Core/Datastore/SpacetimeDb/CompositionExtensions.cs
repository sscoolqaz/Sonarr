using DryIoc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Opt-in override that swaps a growing set of SQL-backed repositories for their
    /// SpacetimeDB-backed equivalents, gated behind config so the rest of the app is unaffected
    /// unless explicitly enabled. Phase 4 adds one entity at a time here as each is ported -
    /// everything not yet listed is still on the normal SQLite path.
    /// </summary>
    public static class CompositionExtensions
    {
        public static IContainer AddSpacetimeDbRepositories(this IContainer container, string host, string database)
        {
            container.RegisterInstance<ISpacetimeDbConnection>(
                new SpacetimeDbConnection(host, database),
                ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<ITagRepository, SpacetimeTagRepository>(container);
            Register<IRootFolderRepository, SpacetimeRootFolderRepository>(container);
            Register<IRemotePathMappingRepository, SpacetimeRemotePathMappingRepository>(container);
            Register<IImportListExclusionRepository, SpacetimeImportListExclusionRepository>(container);
            Register<ICustomFilterRepository, SpacetimeCustomFilterRepository>(container);
            Register<IUserRepository, SpacetimeUserRepository>(container);
            Register<IConfigRepository, SpacetimeConfigRepository>(container);
            Register<IQualityProfileRankRepository, SpacetimeQualityProfileRankRepository>(container);
            Register<IDelayProfileRepository, SpacetimeDelayProfileRepository>(container);
            Register<IRestrictionRepository, SpacetimeReleaseProfileRepository>(container);
            Register<IQualityDefinitionRepository, SpacetimeQualityDefinitionRepository>(container);
            Register<INamingConfigRepository, SpacetimeNamingConfigRepository>(container);
            Register<ISceneMappingRepository, SpacetimeSceneMappingRepository>(container);
            Register<IScheduledTaskRepository, SpacetimeScheduledTaskRepository>(container);

            return container;
        }

        private static void Register<TService, TImpl>(IContainer container)
            where TImpl : TService
        {
            container.Register<TService, TImpl>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        }
    }
}
