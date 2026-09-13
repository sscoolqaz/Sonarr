using System;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;

namespace Sonarr.SpacetimeMigration
{
    // One-time tool for cutting an existing SQLite-backed Sonarr install over to SpacetimeDB.
    // First-pass scope: Tag, QualityProfile, Series (+ tags), EpisodeFile, Episode,
    // EpisodeHistory, Blocklist - the "core" entities, extend the same pattern to the rest of
    // the port's ~40 entities later.
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

            Console.WriteLine(options.DryRun
                ? "Dry run - reading source data only, nothing will be written to SpacetimeDB."
                : $"Migrating into SpacetimeDB database '{options.TargetDatabase}' at {options.TargetHost}.");

            using var target = options.DryRun ? null : new SpacetimeDbConnection(options.TargetHost, options.TargetDatabase);

            if (target != null)
            {
                RequireEmptyTarget(target);
            }

            var targetTags = target == null ? null : new SpacetimeTagRepository(target, eventAggregator);

            // customFormatService here is never actually invoked by MigrateInsert (only by the
            // normal ToModel/read path this tool never calls on the target side) - reusing the
            // source-side instance just satisfies the constructor.
            var targetQualityProfiles = target == null ? null : new SpacetimeQualityProfileRepository(target, eventAggregator, customFormatService);
            var targetSeries = target == null ? null : new SpacetimeSeriesRepository(target, eventAggregator, targetQualityProfiles);
            var targetMediaFiles = target == null ? null : new SpacetimeMediaFileRepository(target, eventAggregator);
            var targetEpisodes = target == null ? null : new SpacetimeEpisodeRepository(target, eventAggregator, targetMediaFiles, targetSeries, null);
            var targetHistory = target == null ? null : new SpacetimeHistoryRepository(target, eventAggregator, targetSeries, targetEpisodes, null);
            var targetBlocklist = target == null ? null : new SpacetimeBlocklistRepository(target, eventAggregator, targetSeries, null);

            MigrateEntity(
                "Tags",
                tagRepository.All(),
                options,
                model => targetTags?.MigrateInsert(model),
                () => targetTags.Count());

            MigrateEntity(
                "QualityProfiles",
                qualityProfileRepository.All(),
                options,
                model => targetQualityProfiles?.MigrateInsert(model),
                () => targetQualityProfiles.Count());

            var expectedSeriesTagRows = 0;

            MigrateEntity(
                "Series",
                seriesRepository.All(),
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
                () => targetSeries.Count());

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
                mediaFileRepository.All(),
                options,
                model => targetMediaFiles?.MigrateInsert(model),
                () => targetMediaFiles.Count());

            MigrateEntity(
                "Episodes",
                episodeRepository.All(),
                options,
                model => targetEpisodes?.MigrateInsert(model),
                () => targetEpisodes.Count());

            MigrateEntity(
                "History",
                historyRepository.All(),
                options,
                model => targetHistory?.MigrateInsert(model),
                () => targetHistory.Count());

            MigrateEntity(
                "Blocklist",
                blocklistRepository.All(),
                options,
                model => targetBlocklist?.MigrateInsert(model),
                () => targetBlocklist.Count());

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
        private static void RequireEmptyTarget(SpacetimeDbConnection target)
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
    }
}
