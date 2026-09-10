using DryIoc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Update.History;

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

            // Tier 1
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
            Register<IDownloadHistoryRepository, SpacetimeDownloadHistoryRepository>(container);
            Register<ICustomFormatRepository, SpacetimeCustomFormatRepository>(container);
            Register<IAutoTaggingRepository, SpacetimeAutoTaggingRepository>(container);
            Register<IImportListItemRepository, SpacetimeImportListItemRepository>(container);
            Register<ICommandRepository, SpacetimeCommandRepository>(container);
            Register<IPendingReleaseRepository, SpacetimePendingReleaseRepository>(container);
            Register<IUpdateHistoryRepository, SpacetimeUpdateHistoryRepository>(container);
            Register<IOtherExtraFileRepository, SpacetimeOtherExtraFileRepository>(container);
            Register<ISubtitleFileRepository, SpacetimeSubtitleFileRepository>(container);
            Register<IMetadataFileRepository, SpacetimeMetadataFileRepository>(container);
            Register<IIndexerStatusRepository, SpacetimeIndexerStatusRepository>(container);
            Register<IDownloadClientStatusRepository, SpacetimeDownloadClientStatusRepository>(container);
            Register<IImportListStatusRepository, SpacetimeImportListStatusRepository>(container);
            Register<INotificationStatusRepository, SpacetimeNotificationStatusRepository>(container);
            Register<IMediaFileRepository, SpacetimeMediaFileRepository>(container);

            // Tier 1.5
            Register<IIndexerRepository, SpacetimeIndexerRepository>(container);
            Register<IImportListRepository, SpacetimeImportListRepository>(container);
            Register<INotificationRepository, SpacetimeNotificationRepository>(container);
            Register<IDownloadClientRepository, SpacetimeDownloadClientRepository>(container);
            Register<IMetadataRepository, SpacetimeMetadataRepository>(container);

            return container;
        }

        private static void Register<TService, TImpl>(IContainer container)
            where TImpl : TService
        {
            container.Register<TService, TImpl>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        }
    }
}
