using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.Exclusions;
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
using NzbDrone.Core.Tv;
using NzbDrone.Core.Update.History;

namespace Sonarr.SpacetimeMigration
{
    // One-time tool for cutting an existing SQLite-backed Sonarr install over to SpacetimeDB.
    // Second-pass scope adds every remaining entity except the entities deliberately left out of
    // the Spacetime housekeeping-task audit's "safe to leave SQL-only" bucket for the same
    // ephemeral/recreatable reasons: ImportListItem (repopulated on next list sync), SceneMapping
    // (repopulated from its external source), QualityProfileQualityRank (a derived cache -
    // QualityProfileRankService.SeedAll computes and persists one for any profile that doesn't
    // already have it at every ApplicationStartedEvent, and UpdateRanksForProfile recomputes it
    // again on every subsequent profile save), and the four provider Status tables
    // (IndexerStatus/DownloadClientStatus/ImportListStatus/NotificationStatus - transient
    // health/backoff tracking that self-heals within a health-check cycle).
    //
    // Command is migrated nowhere (no MigrateInsert override, no reducer) - instead
    // RequireNoPendingCommands below refuses to proceed if the source has any Queued or Started
    // command. This isn't the same "safe to skip" reasoning as the entities above:
    // CommandQueueManager.Requeue() reloads every still-Queued command from the repository at
    // every ApplicationStartedEvent (a normal Sonarr restart does NOT drain the queue), so a
    // command genuinely in flight at migration time would be silently lost, not recreated. The
    // fix is operational, not code - let the source instance's queue drain (check its Activity/
    // Queue page) before migrating, rather than teaching this tool to migrate a job queue.
    //
    // UpdateHistory is migrated only when --source-log is given - unlike every other entity here,
    // it lives in the source's separate logs.db file (its own real repository is constructed
    // with ILogDatabase, not IMainDatabase), and SCHEMA_DESIGN.md is explicit that dropping it is
    // a real feature regression (previous-version detection, installed-update annotations), not a
    // cost-free one - so unlike Command/ImportListItem/etc. this is deliberately opt-in rather
    // than silently skipped: the tool tells you plainly whether it did or didn't migrate it.
    //
    // Deliberately reuses the real, already-tested repository classes to read (guaranteeing
    // correct interpretation of every custom JSON/int-converter shape already in the source
    // data - Quality/Language int-only storage expanded to full objects, CustomFormat ids
    // rehydrated, etc. - entirely for free, the same way the real running app already does it
    // every day) and each SpacetimeXRepository's own MigrateInsert (added alongside this tool -
    // see SpacetimeBasicRepository.MigrateInsert's doc comment) to write. This file contains no
    // bespoke JSON-shape or Quality/Language lookup logic of its own for exactly that reason -
    // duplicating that here would be one more place for the two sides to silently drift apart.
    //
    // The source connection is opened Mode=ReadOnly - this tool cannot write to the source
    // database under any code path. Point it at a `.backup`-produced snapshot copy of the real
    // sonarr.db, not the live file directly, to get a transactionally consistent read even while
    // the source Sonarr instance is running in WAL mode (see README.md in this project for the
    // exact snapshot command).
    public static class Program
    {
        public static int Main(string[] args)
        {
            MigrationOptions options;

            try
            {
                options = MigrationOptions.Parse(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine(MigrationOptions.Usage);
                return 1;
            }

            TableMapping.Map();

            var eventAggregator = new NoOpEventAggregator();
            var source = OpenReadOnlySource(options.SourceSqlitePath);

            var tagRepository = new TagRepository(source, eventAggregator);
            var customFormatRepository = new CustomFormatRepository(source, eventAggregator);
            var customFormatService = new CustomFormatService(customFormatRepository, new CacheManager(), eventAggregator);
            var qualityProfileRepository = new QualityProfileRepository(source, eventAggregator, customFormatService);
            var seriesRepository = new SeriesRepository(source, eventAggregator);
            var episodeRepository = new EpisodeRepository(source, eventAggregator, LogManager.GetLogger("Migration.EpisodeRepository"));
            var mediaFileRepository = new MediaFileRepository(source, eventAggregator);
            var historyRepository = new HistoryRepository(source, eventAggregator);
            var blocklistRepository = new BlocklistRepository(source, eventAggregator);

            var configRepository = new ConfigRepository(source, eventAggregator);
            var namingConfigRepository = new NamingConfigRepository(source, eventAggregator);
            var rootFolderRepository = new RootFolderRepository(source, eventAggregator);
            var remotePathMappingRepository = new RemotePathMappingRepository(source, eventAggregator);
            var customFilterRepository = new CustomFilterRepository(source, eventAggregator);
            var delayProfileRepository = new DelayProfileRepository(source, eventAggregator);
            var releaseProfileRepository = new ReleaseProfileRepository(source, eventAggregator);
            var indexerRepository = new IndexerRepository(source, eventAggregator);
            var downloadClientRepository = new DownloadClientRepository(source, eventAggregator);
            var importListRepository = new ImportListRepository(source, eventAggregator);
            var notificationRepository = new NotificationRepository(source, eventAggregator);
            var metadataRepository = new MetadataRepository(source, eventAggregator);
            var importListExclusionRepository = new ImportListExclusionRepository(source, eventAggregator);
            var qualityDefinitionRepository = new QualityDefinitionRepository(source, eventAggregator);
            var autoTaggingRepository = new AutoTaggingRepository(source, eventAggregator);
            var userRepository = new UserRepository(source, eventAggregator);
            var pendingReleaseRepository = new PendingReleaseRepository(source, eventAggregator);
            var downloadHistoryRepository = new DownloadHistoryRepository(source, eventAggregator);
            var metadataFileRepository = new MetadataFileRepository(source, eventAggregator);
            var subtitleFileRepository = new SubtitleFileRepository(source, eventAggregator);
            var otherExtraFileRepository = new OtherExtraFileRepository(source, eventAggregator);
            var scheduledTaskRepository = new ScheduledTaskRepository(source, eventAggregator);

            var commandRepository = new CommandRepository(source, eventAggregator);
            RequireNoPendingCommands(commandRepository);

            var sourceLog = string.IsNullOrEmpty(options.SourceLogSqlitePath) ? null : OpenReadOnlyLogSource(options.SourceLogSqlitePath);
            var updateHistoryRepository = sourceLog == null ? null : new UpdateHistoryRepository(sourceLog, eventAggregator);

            if (sourceLog == null)
            {
                Console.WriteLine("UpdateHistory: skipped (no --source-log given) - see README.md if you want previous-version detection preserved across the cutover.");
            }

            Console.WriteLine(options.DryRun
                ? "Dry run - reading source data only, nothing will be written to SpacetimeDB."
                : $"Migrating into SpacetimeDB database '{options.TargetDatabase}' at {options.TargetHost}.");

            using var target = options.DryRun ? null : new SpacetimeDbConnection(options.TargetHost, options.TargetDatabase);

            if (target != null)
            {
                RequireEmptyTarget(target, checkUpdateHistory: sourceLog != null);
            }

            var targetTags = target == null ? null : new SpacetimeTagRepository(target, eventAggregator);

            // SpacetimeQualityProfileRepository/SpacetimeSeriesRepository no longer take an
            // ICustomFormatService/IQualityProfileRepository constructor dependency - both now
            // read their sibling table (CustomFormat / QualityProfile) directly off
            // Conn.Connection.Db from within ToModel instead of re-entering another
            // repository's/service's own public API (see those classes' ToModel remarks).
            var targetQualityProfiles = target == null ? null : new SpacetimeQualityProfileRepository(target, eventAggregator);
            var targetSeries = target == null ? null : new SpacetimeSeriesRepository(target, eventAggregator);
            var targetMediaFiles = target == null ? null : new SpacetimeMediaFileRepository(target, eventAggregator);
            var targetEpisodes = target == null ? null : new SpacetimeEpisodeRepository(target, eventAggregator, targetMediaFiles, targetSeries, null);
            var targetHistory = target == null ? null : new SpacetimeHistoryRepository(target, eventAggregator, targetSeries, targetEpisodes, null);
            var targetBlocklist = target == null ? null : new SpacetimeBlocklistRepository(target, eventAggregator, targetSeries, null);

            var targetCustomFormats = target == null ? null : new SpacetimeCustomFormatRepository(target, eventAggregator);
            var targetConfig = target == null ? null : new SpacetimeConfigRepository(target, eventAggregator);
            var targetNamingConfig = target == null ? null : new SpacetimeNamingConfigRepository(target, eventAggregator);
            var targetRootFolders = target == null ? null : new SpacetimeRootFolderRepository(target, eventAggregator);
            var targetRemotePathMappings = target == null ? null : new SpacetimeRemotePathMappingRepository(target, eventAggregator);
            var targetCustomFilters = target == null ? null : new SpacetimeCustomFilterRepository(target, eventAggregator);
            var targetDelayProfiles = target == null ? null : new SpacetimeDelayProfileRepository(target, eventAggregator);
            var targetReleaseProfiles = target == null ? null : new SpacetimeReleaseProfileRepository(target, eventAggregator);
            var targetIndexers = target == null ? null : new SpacetimeIndexerRepository(target, eventAggregator);
            var targetDownloadClients = target == null ? null : new SpacetimeDownloadClientRepository(target, eventAggregator);
            var targetImportLists = target == null ? null : new SpacetimeImportListRepository(target, eventAggregator);
            var targetNotifications = target == null ? null : new SpacetimeNotificationRepository(target, eventAggregator);
            var targetMetadata = target == null ? null : new SpacetimeMetadataRepository(target, eventAggregator);
            var targetImportListExclusions = target == null ? null : new SpacetimeImportListExclusionRepository(target, eventAggregator);
            var targetQualityDefinitions = target == null ? null : new SpacetimeQualityDefinitionRepository(target, eventAggregator);
            var targetAutoTagging = target == null ? null : new SpacetimeAutoTaggingRepository(target, eventAggregator);
            var targetUsers = target == null ? null : new SpacetimeUserRepository(target, eventAggregator);
            var targetPendingReleases = target == null ? null : new SpacetimePendingReleaseRepository(target, eventAggregator);
            var targetDownloadHistory = target == null ? null : new SpacetimeDownloadHistoryRepository(target, eventAggregator);
            var targetMetadataFiles = target == null ? null : new SpacetimeMetadataFileRepository(target, eventAggregator);
            var targetSubtitleFiles = target == null ? null : new SpacetimeSubtitleFileRepository(target, eventAggregator);
            var targetOtherExtraFiles = target == null ? null : new SpacetimeOtherExtraFileRepository(target, eventAggregator);
            var targetScheduledTasks = target == null ? null : new SpacetimeScheduledTaskRepository(target, eventAggregator);
            var targetUpdateHistory = target == null || sourceLog == null ? null : new SpacetimeUpdateHistoryRepository(target, eventAggregator);

            MigrateEntity(
                "Tags",
                tagRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetTags?.MigrateInsert(model),
                () => targetTags.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "QualityProfiles",
                qualityProfileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetQualityProfiles?.MigrateInsert(model),
                () => targetQualityProfiles.Count().GetAwaiter().GetResult());

            var expectedSeriesTagRows = 0;

            MigrateEntity(
                "Series",
                seriesRepository.All().GetAwaiter().GetResult(),
                options,
                model =>
                {
                    targetSeries?.MigrateInsert(model);

                    if (target != null && model.Tags.Count > 0)
                    {
                        target.Connection.Reducers.ReplaceSeriesTags(model.Id, model.Tags.ToList());
                        expectedSeriesTagRows += model.Tags.Count;
                    }
                },
                () => targetSeries.Count().GetAwaiter().GetResult());

            // ReplaceSeriesTags is a reducer call like any MigrateInsert - just as fire-and-forget,
            // and not covered by the Count() check above (that only confirms the Series rows
            // themselves, not the SeriesTag junction rows the same loop also queued). Verify those
            // separately the same way: poll the actual server-side row count via RemoteQuery
            // (bypasses the FrameTick-dependent write-confirmation path the same way Count() does
            // for every other entity) rather than trusting that the reducer call not throwing
            // means the write landed.
            if (target != null)
            {
                WaitForCount("SeriesTag", expectedSeriesTagRows, () => target.Connection.Db.SeriesTag.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            }

            MigrateEntity(
                "EpisodeFiles",
                mediaFileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetMediaFiles?.MigrateInsert(model),
                () => targetMediaFiles.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "Episodes",
                episodeRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetEpisodes?.MigrateInsert(model),
                () => targetEpisodes.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "History",
                historyRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetHistory?.MigrateInsert(model),
                () => targetHistory.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "Blocklist",
                blocklistRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetBlocklist?.MigrateInsert(model),
                () => targetBlocklist.Count().GetAwaiter().GetResult());

            // Second migration pass - everything else this port has ported, except UpdateHistory
            // and the deliberately-excluded entities (see this file's own top-of-file comment for
            // why). No foreign-key enforcement exists on the SpacetimeDB side, so unlike Series/
            // SeriesTag above there's no ordering requirement between any of these - grouped here
            // roughly by real-app subsystem rather than by dependency.
            MigrateEntity(
                "CustomFormats",
                customFormatRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetCustomFormats?.MigrateInsert(model),
                () => targetCustomFormats.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "Config",
                configRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetConfig?.MigrateInsert(model),
                () => targetConfig.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "NamingConfig",
                namingConfigRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetNamingConfig?.MigrateInsert(model),
                () => targetNamingConfig.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "RootFolders",
                rootFolderRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetRootFolders?.MigrateInsert(model),
                () => targetRootFolders.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "RemotePathMappings",
                remotePathMappingRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetRemotePathMappings?.MigrateInsert(model),
                () => targetRemotePathMappings.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "CustomFilters",
                customFilterRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetCustomFilters?.MigrateInsert(model),
                () => targetCustomFilters.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "DelayProfiles",
                delayProfileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetDelayProfiles?.MigrateInsert(model),
                () => targetDelayProfiles.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "ReleaseProfiles",
                releaseProfileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetReleaseProfiles?.MigrateInsert(model),
                () => targetReleaseProfiles.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "Indexers",
                indexerRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetIndexers?.MigrateInsert(model),
                () => targetIndexers.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "DownloadClients",
                downloadClientRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetDownloadClients?.MigrateInsert(model),
                () => targetDownloadClients.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "ImportLists",
                importListRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetImportLists?.MigrateInsert(model),
                () => targetImportLists.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "Notifications",
                notificationRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetNotifications?.MigrateInsert(model),
                () => targetNotifications.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "MetadataProviders",
                metadataRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetMetadata?.MigrateInsert(model),
                () => targetMetadata.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "ImportListExclusions",
                importListExclusionRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetImportListExclusions?.MigrateInsert(model),
                () => targetImportListExclusions.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "QualityDefinitions",
                qualityDefinitionRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetQualityDefinitions?.MigrateInsert(model),
                () => targetQualityDefinitions.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "AutoTagging",
                autoTaggingRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetAutoTagging?.MigrateInsert(model),
                () => targetAutoTagging.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "Users",
                userRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetUsers?.MigrateInsert(model),
                () => targetUsers.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "PendingReleases",
                pendingReleaseRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetPendingReleases?.MigrateInsert(model),
                () => targetPendingReleases.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "DownloadHistory",
                downloadHistoryRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetDownloadHistory?.MigrateInsert(model),
                () => targetDownloadHistory.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "MetadataFiles",
                metadataFileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetMetadataFiles?.MigrateInsert(model),
                () => targetMetadataFiles.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "SubtitleFiles",
                subtitleFileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetSubtitleFiles?.MigrateInsert(model),
                () => targetSubtitleFiles.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "OtherExtraFiles",
                otherExtraFileRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetOtherExtraFiles?.MigrateInsert(model),
                () => targetOtherExtraFiles.Count().GetAwaiter().GetResult());

            MigrateEntity(
                "ScheduledTasks",
                scheduledTaskRepository.All().GetAwaiter().GetResult(),
                options,
                model => targetScheduledTasks?.MigrateInsert(model),
                () => targetScheduledTasks.Count().GetAwaiter().GetResult());

            if (sourceLog != null)
            {
                MigrateEntity(
                    "UpdateHistory",
                    updateHistoryRepository.All().GetAwaiter().GetResult(),
                    options,
                    model => targetUpdateHistory?.MigrateInsert(model),
                    () => targetUpdateHistory.Count().GetAwaiter().GetResult());
            }

            Console.WriteLine("Done.");

            return 0;
        }

        // MigrateInsert calls a reducer over the SpacetimeDB C# SDK's connection, which is
        // fire-and-forget from this call site's point of view - it queues the request and
        // returns immediately, with the actual write happening (and only then becoming visible
        // to a query) once the connection's background FrameTick pump processes the server's
        // response, asynchronously and later. A tight loop of thousands of these can out-run that
        // pump: confirmed empirically against a real 76MB production snapshot - every entity
        // *reported* 0 failures, but the target database's actual row count for the last two
        // entities processed (History, Blocklist) didn't match at all (1639/5088 and 0/305
        // respectively) once the process had already moved on and disposed the connection. So
        // "the call didn't throw" is not evidence the write happened - only polling the target's
        // own row count after the loop, and refusing to report success until it actually matches,
        // is.
        private static void MigrateEntity<T>(string name, System.Collections.Generic.IEnumerable<T> rows, MigrationOptions options, Action<T> migrate, Func<int> targetCount = null)
        {
            var count = 0;
            var errors = 0;

            foreach (var row in options.Limit.HasValue ? rows.Take(options.Limit.Value) : rows)
            {
                count++;

                try
                {
                    migrate(row);
                }
                catch (Exception ex)
                {
                    errors++;
                    Console.Error.WriteLine($"[{name}] row {count} failed: {ex.Message}");

                    if (!options.ContinueOnError)
                    {
                        throw new InvalidOperationException($"Migration of {name} failed at row {count} (pass --continue-on-error to skip failures instead of stopping)", ex);
                    }
                }
            }

            var expected = count - errors;

            if (targetCount != null && !options.DryRun)
            {
                WaitForCount(name, expected, targetCount);
                Console.WriteLine($"{name}: {count} read, {expected} migrated and confirmed in the target database, {errors} failed.");
                return;
            }

            Console.WriteLine($"{name}: {count} read, {expected} migrated, {errors} failed.");
        }

        private static void WaitForCount(string name, int expected, Func<int> actualCount)
        {
            var deadline = DateTime.UtcNow.AddMinutes(5);
            int actual;

            while ((actual = actualCount()) < expected && DateTime.UtcNow < deadline)
            {
                Console.WriteLine($"{name}: waiting for SpacetimeDB to catch up ({actual}/{expected} confirmed so far)...");
                Thread.Sleep(1000);
            }

            if (actual != expected)
            {
                throw new InvalidOperationException(
                    $"{name}: migration did not converge - {expected} rows were sent but only {actual} are confirmed in the target database after waiting 5 minutes. " +
                    "Do not proceed to the next entity or trust this run's overall result; investigate before retrying.");
            }
        }

        // Guards against the false-success mode where an already-populated target's row counts
        // happen to equal (or already exceed) what this run expects, even though this run's own
        // MigrateInsert calls actually failed (SpacetimeDB rejects an insert with a duplicate
        // primary key - reducer failures are async/silent here just like successes are, so
        // WaitForCount's convergence check alone can't tell "already had N rows before we
        // started" apart from "our N inserts landed"). Every entity this tool covers must start
        // genuinely empty - point --target-db at a fresh database, not one that's been migrated
        // into before or that has any other data in it.
        private static void RequireEmptyTarget(SpacetimeDbConnection target, bool checkUpdateHistory)
        {
            void CheckEmpty(string name, Func<int> count)
            {
                var existing = count();

                if (existing > 0)
                {
                    throw new InvalidOperationException(
                        $"Target database already has {existing} existing {name} row(s) - this tool requires a fresh, empty SpacetimeDB database " +
                        "(row-count-based write verification can't tell pre-existing rows apart from this run's own writes). " +
                        "Use --target-db with a database name that has never been migrated into.");
                }
            }

            CheckEmpty("Tag", () => target.Connection.Db.Tag.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("QualityProfile", () => target.Connection.Db.QualityProfile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("Series", () => target.Connection.Db.Series.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("SeriesTag", () => target.Connection.Db.SeriesTag.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("EpisodeFile", () => target.Connection.Db.EpisodeFile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("Episode", () => target.Connection.Db.Episode.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("EpisodeHistory", () => target.Connection.Db.EpisodeHistory.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("Blocklist", () => target.Connection.Db.Blocklist.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);

            CheckEmpty("CustomFormat", () => target.Connection.Db.CustomFormat.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);

            // Config/User are private tables (see the SECURITY comments on them in
            // Sonarr.SpacetimeModule/spacetimedb/Batch1.cs) - this migration tool's own
            // connection reads them, like any other client, through the TrustedConfigs/
            // TrustedUsers views rather than the tables directly. Row shape and count are
            // identical to the underlying table, so this emptiness check is unaffected.
            CheckEmpty("Config", () => target.Connection.Db.TrustedConfigs.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("NamingConfig", () => target.Connection.Db.NamingConfig.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("RootFolder", () => target.Connection.Db.RootFolder.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("RemotePathMapping", () => target.Connection.Db.RemotePathMapping.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("CustomFilter", () => target.Connection.Db.CustomFilter.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("DelayProfile", () => target.Connection.Db.DelayProfile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("ReleaseProfile", () => target.Connection.Db.ReleaseProfile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("IndexerDefinition", () => target.Connection.Db.IndexerDefinition.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("DownloadClientDefinition", () => target.Connection.Db.DownloadClientDefinition.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("ImportListDefinition", () => target.Connection.Db.ImportListDefinition.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("NotificationDefinition", () => target.Connection.Db.NotificationDefinition.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("MetadataDefinition", () => target.Connection.Db.MetadataDefinition.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("ImportListExclusion", () => target.Connection.Db.ImportListExclusion.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("QualityDefinition", () => target.Connection.Db.QualityDefinition.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("AutoTag", () => target.Connection.Db.AutoTag.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("User", () => target.Connection.Db.TrustedUsers.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("PendingRelease", () => target.Connection.Db.PendingRelease.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("DownloadHistory", () => target.Connection.Db.DownloadHistory.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("MetadataFile", () => target.Connection.Db.MetadataFile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("SubtitleFile", () => target.Connection.Db.SubtitleFile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("OtherExtraFile", () => target.Connection.Db.OtherExtraFile.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            CheckEmpty("ScheduledTask", () => target.Connection.Db.ScheduledTask.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);

            if (checkUpdateHistory)
            {
                CheckEmpty("UpdateHistory", () => target.Connection.Db.UpdateHistory.RemoteQuery(string.Empty).GetAwaiter().GetResult().Length);
            }
        }

        private static IMainDatabase OpenReadOnlySource(string sqlitePath)
        {
            var connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                ReadOnly = true,
                JournalMode = SQLiteJournalModeEnum.Wal
            }.ConnectionString;

            var database = new Database("Source", () =>
            {
                var connection = SQLiteFactory.Instance.CreateConnection();
                connection.ConnectionString = connectionString;
                connection.Open();
                return connection;
            });

            return new MainDatabase(database);
        }

        private static ILogDatabase OpenReadOnlyLogSource(string sqlitePath)
        {
            var connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                ReadOnly = true,
                JournalMode = SQLiteJournalModeEnum.Wal
            }.ConnectionString;

            var database = new Database("SourceLog", () =>
            {
                var connection = SQLiteFactory.Instance.CreateConnection();
                connection.ConnectionString = connectionString;
                connection.Open();
                return connection;
            });

            return new LogDatabase(database);
        }

        // CommandQueueManager.Requeue() reloads every still-Queued command from the repository at
        // every ApplicationStartedEvent, and Started commands are only flipped to Orphaned by
        // OrphanStartedCommands at that same startup point - so unlike a normal restart, a
        // command genuinely in flight (Queued or Started) at migration time has no path back:
        // this tool never migrates Command at all, so it would simply vanish. Refuse to proceed
        // rather than silently drop real in-flight work - see this file's own top-of-file comment
        // for the full reasoning.
        private static void RequireNoPendingCommands(ICommandRepository commandRepository)
        {
            var pending = commandRepository.All().GetAwaiter().GetResult()
                .Where(c => c.Status == CommandStatus.Queued || c.Status == CommandStatus.Started)
                .ToList();

            if (pending.Any())
            {
                throw new InvalidOperationException(
                    $"Source database has {pending.Count} command(s) still Queued or Started (e.g. \"{pending[0].Name}\") - " +
                    "this tool does not migrate the Command table, so an in-flight command would be silently lost, not recreated " +
                    "(a normal Sonarr restart reloads Queued commands from the database; this migration does not carry that table over at all). " +
                    "Wait for the source instance's command queue to fully drain (check its Activity/Queue page) and retry.");
            }
        }
    }
}
