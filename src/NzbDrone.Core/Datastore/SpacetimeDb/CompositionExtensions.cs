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
    /// <summary>
    /// Opt-in override that swaps a growing set of SQL-backed repositories for their
    /// SpacetimeDB-backed equivalents, gated behind config so the rest of the app is unaffected
    /// unless explicitly enabled. Phase 4 adds one entity at a time here as each is ported -
    /// everything not yet listed is still on the normal SQLite path.
    /// </summary>
    public static class CompositionExtensions
    {
        // AutoAddServices' reflection scan (NzbDrone.Common/Composition/Extensions.cs) registers
        // every public IHousekeepingTask implementation in the assembly unconditionally, before
        // the SpacetimeDb-enabled check even runs - these Spacetime*-prefixed classes included.
        // Left alone, they'd stay registered and actively run (deleting real rows through their
        // injected repositories, which resolve to the real SQL-backed ones) even when SpacetimeDb
        // mode is off, breaking this whole class' "opt-in, rest of the app unaffected" contract.
        // RemoveAutoRegisteredHousekeepingTasks must be called unconditionally, right after
        // AutoAddServices and regardless of the SpacetimeDb flag, to strip them back out; only
        // AddSpacetimeDbRepositories (itself flag-gated) adds them back.
        private static readonly System.Type[] SpacetimeHousekeepingTaskTypes =
        {
            typeof(SpacetimeCleanupOrphanedEpisodes),
            typeof(SpacetimeCleanupOrphanedEpisodeFiles),
            typeof(SpacetimeCleanupOrphanedBlocklist),
            typeof(SpacetimeCleanupOrphanedHistoryItems),
            typeof(SpacetimeCleanupOrphanedPendingReleases),
            typeof(SpacetimeCleanupOrphanedExtraFiles),
            typeof(SpacetimeCleanupOrphanedMetadataFiles),
            typeof(SpacetimeCleanupOrphanedSubtitleFiles),
            typeof(SpacetimeCleanupOrphanedIndexerStatus),
            typeof(SpacetimeCleanupOrphanedDownloadClientStatus),
            typeof(SpacetimeCleanupOrphanedImportListStatus),
            typeof(SpacetimeCleanupOrphanedNotificationStatus)
        };

        public static IContainer RemoveAutoRegisteredHousekeepingTasks(this IContainer container)
        {
            foreach (var type in SpacetimeHousekeepingTaskTypes)
            {
                container.Unregister(typeof(IHousekeepingTask), condition: f => f.ImplementationType == type);
            }

            return container;
        }

        // AutoAddServices' reflection scan registers every public concrete class against every
        // interface it implements, unconditionally - by the time any entity here had a second
        // (Spacetime-backed) implementation added, the real SQL-backed one and its Spacetime
        // counterpart both became registered under the same single-instance service interface
        // (e.g. ITagRepository -> TagRepository AND SpacetimeTagRepository), with no SpacetimeDb
        // flag involved yet. AddSpacetimeDbRepositories's Register<TService,TImpl> below reliably
        // pins the Spacetime side when the flag is on (Replace guarantees it wins regardless of
        // scan order), but when the flag is off, nothing ever pins the real side back - DryIoc's
        // SelectLastRegisteredFactory rule (see Bootstrap.cs's container Rules) then resolves
        // whichever of the two the reflection scan happened to register last, which depends on
        // assembly type-enumeration order, not on which one is actually correct to use. This was
        // never exercised until the SpacetimeDb-disabled path was tested directly: it surfaced as
        // IUpdateHistoryRepository and ICommandRepository resolving to their Spacetime versions
        // with SpacetimeDb disabled, throwing on ISpacetimeDbConnection's unresolvable "host"
        // constructor argument (that instance is only ever registered inside
        // AddSpacetimeDbRepositories) - breaking ApplicationStartedEvent handling and the entire
        // /api/v3/command pipeline respectively. This must run unconditionally, right after
        // AutoAddServices and before the SpacetimeDb-enabled check, mirroring
        // RemoveAutoRegisteredHousekeepingTasks's placement - it pins the *real* implementation
        // for every entity that has a Spacetime counterpart, so resolution is deterministic
        // whether or not AddSpacetimeDbRepositories runs afterward to flip it back.
        public static IContainer PinRealRepositoriesByDefault(this IContainer container)
        {
            Register<ITagRepository, TagRepository>(container);
            Register<IRootFolderRepository, RootFolderRepository>(container);
            Register<IRemotePathMappingRepository, RemotePathMappingRepository>(container);
            Register<IImportListExclusionRepository, ImportListExclusionRepository>(container);
            Register<ICustomFilterRepository, CustomFilterRepository>(container);
            Register<IUserRepository, UserRepository>(container);
            Register<IConfigRepository, ConfigRepository>(container);
            Register<IQualityProfileRankRepository, QualityProfileRankRepository>(container);
            Register<IDelayProfileRepository, DelayProfileRepository>(container);
            Register<IRestrictionRepository, ReleaseProfileRepository>(container);
            Register<IQualityDefinitionRepository, QualityDefinitionRepository>(container);
            Register<INamingConfigRepository, NamingConfigRepository>(container);
            Register<ISceneMappingRepository, SceneMappingRepository>(container);
            Register<IScheduledTaskRepository, ScheduledTaskRepository>(container);
            Register<IDownloadHistoryRepository, DownloadHistoryRepository>(container);
            Register<ICustomFormatRepository, CustomFormatRepository>(container);
            Register<IAutoTaggingRepository, AutoTaggingRepository>(container);
            Register<IImportListItemRepository, ImportListItemRepository>(container);
            Register<ICommandRepository, CommandRepository>(container);
            Register<IPendingReleaseRepository, PendingReleaseRepository>(container);
            Register<IUpdateHistoryRepository, UpdateHistoryRepository>(container);
            Register<IOtherExtraFileRepository, OtherExtraFileRepository>(container);
            Register<ISubtitleFileRepository, SubtitleFileRepository>(container);
            Register<IMetadataFileRepository, MetadataFileRepository>(container);
            Register<IIndexerStatusRepository, IndexerStatusRepository>(container);
            Register<IDownloadClientStatusRepository, DownloadClientStatusRepository>(container);
            Register<IImportListStatusRepository, ImportListStatusRepository>(container);
            Register<INotificationStatusRepository, NotificationStatusRepository>(container);
            Register<IMediaFileRepository, MediaFileRepository>(container);

            Register<IIndexerRepository, IndexerRepository>(container);
            Register<IImportListRepository, ImportListRepository>(container);
            Register<INotificationRepository, NotificationRepository>(container);
            Register<IDownloadClientRepository, DownloadClientRepository>(container);
            Register<IMetadataRepository, MetadataRepository>(container);
            Register<IQualityProfileRepository, QualityProfileRepository>(container);
            Register<ISeriesRepository, SeriesRepository>(container);

            Register<IEpisodeRepository, EpisodeRepository>(container);
            Register<IHistoryRepository, HistoryRepository>(container);
            Register<IBlocklistRepository, BlocklistRepository>(container);

            Register<ISeriesStatisticsRepository, SeriesStatisticsRepository>(container);
            Register<IStatisticsRepository, StatisticsRepository>(container);

            // Several of the above are ALSO independently scanned/ambiguous under a shared
            // generic base interface their own service (e.g. ISubtitleFileRepository) doesn't
            // cover - SubtitleFileService/MetadataFileService/OtherExtraFileService inject
            // IExtraFileRepository<T> directly, not the named interface, and ProviderFactory<T>/
            // ProviderStatusServiceBase<T> do the same for IProviderRepository<T>/
            // IProviderStatusRepository<T>. Pinning ISubtitleFileRepository above does nothing
            // for IExtraFileRepository<SubtitleFile> - it's a separate DryIoc service type scanned
            // and resolved independently, so each needs its own explicit pin here too.
            Register<IExtraFileRepository<SubtitleFile>, SubtitleFileRepository>(container);
            Register<IExtraFileRepository<MetadataFile>, MetadataFileRepository>(container);
            Register<IExtraFileRepository<OtherExtraFile>, OtherExtraFileRepository>(container);

            Register<IProviderRepository<IndexerDefinition>, IndexerRepository>(container);
            Register<IProviderRepository<DownloadClientDefinition>, DownloadClientRepository>(container);
            Register<IProviderRepository<ImportListDefinition>, ImportListRepository>(container);
            Register<IProviderRepository<NotificationDefinition>, NotificationRepository>(container);
            Register<IProviderRepository<MetadataDefinition>, MetadataRepository>(container);

            Register<IProviderStatusRepository<IndexerStatus>, IndexerStatusRepository>(container);
            Register<IProviderStatusRepository<DownloadClientStatus>, DownloadClientStatusRepository>(container);
            Register<IProviderStatusRepository<ImportListStatus>, ImportListStatusRepository>(container);
            Register<IProviderStatusRepository<NotificationStatus>, NotificationStatusRepository>(container);

            return container;
        }

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
            Register<IQualityProfileRepository, SpacetimeQualityProfileRepository>(container);
            Register<ISeriesRepository, SpacetimeSeriesRepository>(container);

            // Tier 2
            Register<IEpisodeRepository, SpacetimeEpisodeRepository>(container);
            Register<IHistoryRepository, SpacetimeHistoryRepository>(container);
            Register<IBlocklistRepository, SpacetimeBlocklistRepository>(container);

            // Outside the entity survey (doesn't inherit SpacetimeBasicRepository<T> - see
            // SpacetimeSeriesStatisticsRepository's own comment)
            Register<ISeriesStatisticsRepository, SpacetimeSeriesStatisticsRepository>(container);
            Register<IStatisticsRepository, SpacetimeStatisticsRepository>(container);

            // See PinRealRepositoriesByDefault's matching comment - these shared generic base
            // interfaces are independently-resolved DryIoc services, not covered by pinning the
            // named interfaces above, on this side of the flag too.
            Register<IExtraFileRepository<SubtitleFile>, SpacetimeSubtitleFileRepository>(container);
            Register<IExtraFileRepository<MetadataFile>, SpacetimeMetadataFileRepository>(container);
            Register<IExtraFileRepository<OtherExtraFile>, SpacetimeOtherExtraFileRepository>(container);

            Register<IProviderRepository<IndexerDefinition>, SpacetimeIndexerRepository>(container);
            Register<IProviderRepository<DownloadClientDefinition>, SpacetimeDownloadClientRepository>(container);
            Register<IProviderRepository<ImportListDefinition>, SpacetimeImportListRepository>(container);
            Register<IProviderRepository<NotificationDefinition>, SpacetimeNotificationRepository>(container);
            Register<IProviderRepository<MetadataDefinition>, SpacetimeMetadataRepository>(container);

            Register<IProviderStatusRepository<IndexerStatus>, SpacetimeIndexerStatusRepository>(container);
            Register<IProviderStatusRepository<DownloadClientStatus>, SpacetimeDownloadClientStatusRepository>(container);
            Register<IProviderStatusRepository<ImportListStatus>, SpacetimeImportListStatusRepository>(container);
            Register<IProviderStatusRepository<NotificationStatus>, SpacetimeNotificationStatusRepository>(container);

            // Orphan-cleanup housekeeping tasks (see SpacetimeCleanupOrphanedEpisodes's own
            // comment for why these are additional IHousekeepingTask registrations, not swapped
            // in place of the real SQL-based ones the way every repository above is).
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

            return container;
        }

        private static void Register<TService, TImpl>(IContainer container)
            where TImpl : TService
        {
            container.Register<TService, TImpl>(Reuse.Singleton, ifAlreadyRegistered: IfAlreadyRegistered.Replace);
        }

        // IHousekeepingTask is a many-implementations-per-service collection (HousekeepingService
        // takes IEnumerable<IHousekeepingTask>, resolving every registered implementation), unlike
        // every 1:1 repository interface above where Register<TService,TImpl>'s Replace policy is
        // enough on its own - a collection service has no single "the" registration for Replace to
        // act on. By the time this runs, RemoveAutoRegisteredHousekeepingTasks has already
        // stripped out whatever AutoAddServices' scan added for this exact implementation type
        // (see that method's comment), so this only ever adds the one registration back.
        private static void RegisterAdditionalHousekeepingTask<TImpl>(IContainer container)
            where TImpl : IHousekeepingTask
        {
            container.Register<IHousekeepingTask, TImpl>(Reuse.Singleton);
        }
    }
}
