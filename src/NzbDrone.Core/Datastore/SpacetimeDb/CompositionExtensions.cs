using DryIoc;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.History;
using NzbDrone.Core.Housekeeping;
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
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Tags;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.ThingiProvider.Status;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public static class CompositionExtensions
    {
        public static IContainer AddSpacetimeDbRepositories(this IContainer container, string host, string database, IOidcTokenProvider tokenProvider = null)
        {
            container.RegisterInstance<ISpacetimeDbConnection>(
                new SpacetimeDbConnection(host, database, tokenProvider),
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
            Register<IDownloadHistoryRepository, SpacetimeDownloadHistoryRepository>(container);
            Register<ICustomFormatRepository, SpacetimeCustomFormatRepository>(container);
            Register<IAutoTaggingRepository, SpacetimeAutoTaggingRepository>(container);
            Register<IImportListItemRepository, SpacetimeImportListItemRepository>(container);
            Register<ICommandRepository, SpacetimeCommandRepository>(container);
            Register<IPendingReleaseRepository, SpacetimePendingReleaseRepository>(container);
            Register<IUpdateHistoryRepository, SpacetimeUpdateHistoryRepository>(container);
            Register<IMediaFileRepository, SpacetimeMediaFileRepository>(container);

            Register<SpacetimeQualityProfileRepository, SpacetimeQualityProfileRepository>(container);
            container.RegisterMapping<IQualityProfileRepository, SpacetimeQualityProfileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<ISeriesRepository, SpacetimeSeriesRepository>(container);

            Register<IEpisodeRepository, SpacetimeEpisodeRepository>(container);
            Register<IHistoryRepository, SpacetimeHistoryRepository>(container);
            Register<IBlocklistRepository, SpacetimeBlocklistRepository>(container);

            Register<ISeriesStatisticsRepository, SpacetimeSeriesStatisticsRepository>(container);
            Register<IStatisticsRepository, SpacetimeStatisticsRepository>(container);

            Register<SpacetimeOtherExtraFileRepository, SpacetimeOtherExtraFileRepository>(container);
            container.RegisterMapping<IOtherExtraFileRepository, SpacetimeOtherExtraFileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IExtraFileRepository<OtherExtraFile>, SpacetimeOtherExtraFileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeSubtitleFileRepository, SpacetimeSubtitleFileRepository>(container);
            container.RegisterMapping<ISubtitleFileRepository, SpacetimeSubtitleFileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IExtraFileRepository<SubtitleFile>, SpacetimeSubtitleFileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeMetadataFileRepository, SpacetimeMetadataFileRepository>(container);
            container.RegisterMapping<IMetadataFileRepository, SpacetimeMetadataFileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IExtraFileRepository<MetadataFile>, SpacetimeMetadataFileRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeIndexerRepository, SpacetimeIndexerRepository>(container);
            container.RegisterMapping<IIndexerRepository, SpacetimeIndexerRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderRepository<IndexerDefinition>, SpacetimeIndexerRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeDownloadClientRepository, SpacetimeDownloadClientRepository>(container);
            container.RegisterMapping<IDownloadClientRepository, SpacetimeDownloadClientRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderRepository<DownloadClientDefinition>, SpacetimeDownloadClientRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeImportListRepository, SpacetimeImportListRepository>(container);
            container.RegisterMapping<IImportListRepository, SpacetimeImportListRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderRepository<ImportListDefinition>, SpacetimeImportListRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeNotificationRepository, SpacetimeNotificationRepository>(container);
            container.RegisterMapping<INotificationRepository, SpacetimeNotificationRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderRepository<NotificationDefinition>, SpacetimeNotificationRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeMetadataRepository, SpacetimeMetadataRepository>(container);
            container.RegisterMapping<IMetadataRepository, SpacetimeMetadataRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderRepository<MetadataDefinition>, SpacetimeMetadataRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeIndexerStatusRepository, SpacetimeIndexerStatusRepository>(container);
            container.RegisterMapping<IIndexerStatusRepository, SpacetimeIndexerStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderStatusRepository<IndexerStatus>, SpacetimeIndexerStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeDownloadClientStatusRepository, SpacetimeDownloadClientStatusRepository>(container);
            container.RegisterMapping<IDownloadClientStatusRepository, SpacetimeDownloadClientStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderStatusRepository<DownloadClientStatus>, SpacetimeDownloadClientStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeImportListStatusRepository, SpacetimeImportListStatusRepository>(container);
            container.RegisterMapping<IImportListStatusRepository, SpacetimeImportListStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderStatusRepository<ImportListStatus>, SpacetimeImportListStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            Register<SpacetimeNotificationStatusRepository, SpacetimeNotificationStatusRepository>(container);
            container.RegisterMapping<INotificationStatusRepository, SpacetimeNotificationStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.RegisterMapping<IProviderStatusRepository<NotificationStatus>, SpacetimeNotificationStatusRepository>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);

            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedEpisodes>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedEpisodeFiles>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedBlocklist>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedHistoryItems>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedPendingReleases>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedExtraFiles>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedMetadataFiles>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedSubtitleFiles>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedIndexerStatus>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedDownloadClientStatus>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedImportListStatus>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupOrphanedNotificationStatus>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupAbsolutePathMetadataFiles>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupDownloadClientUnavailablePendingReleases>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupDuplicateMetadataFiles>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupQualityProfileFormatItems>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeFixFutureRunScheduledTasks>(container);
            RegisterAdditionalHousekeepingTask<SpacetimeCleanupUnusedTags>(container);

            return container;
        }

        private static void Register<TService, TImpl>(IContainer container)
            where TImpl : TService
        {
            container.Register<TService, TImpl>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        }

        private static void RegisterAdditionalHousekeepingTask<TImpl>(IContainer container)
            where TImpl : IHousekeepingTask
        {
            container.Register<IHousekeepingTask, TImpl>(Reuse.Singleton);
        }
    }
}
