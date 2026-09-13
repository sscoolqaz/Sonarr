using System;

namespace Sonarr.SpacetimeMigration
{
    public class MigrationOptions
    {
        public string SourceSqlitePath { get; private set; }
        public string SourceLogSqlitePath { get; private set; }
        public string TargetHost { get; private set; } = "http://127.0.0.1:3000";
        public string TargetDatabase { get; private set; }
        public bool DryRun { get; private set; }
        public bool ContinueOnError { get; private set; }
        public int? Limit { get; private set; }

        public const string Usage =
            "Usage: Sonarr.SpacetimeMigration --source <path-to-sonarr.db> [--source-log <path-to-logs.db>] [--target-host <url>] --target-db <name> [--dry-run] [--continue-on-error] [--limit N]\n\n" +
            "  --source            Path to a read-only SNAPSHOT of the source sonarr.db (see README.md - take one\n" +
            "                      with `sqlite3 sonarr.db \".backup snapshot.db\"` on the source host, don't point\n" +
            "                      this at the live file directly). Opened Mode=ReadOnly regardless.\n" +
            "  --source-log        Path to a read-only SNAPSHOT of the source logs.db, taken the same way as\n" +
            "                      --source. Optional - only needed to migrate UpdateHistory (previous-version\n" +
            "                      detection, installed-update annotations), which SQL Sonarr stores in logs.db\n" +
            "                      rather than sonarr.db. Omit to skip UpdateHistory entirely.\n" +
            "  --target-host       SpacetimeDB host URL. Default http://127.0.0.1:3000.\n" +
            "  --target-db         SpacetimeDB database name to migrate into - use a fresh one, not an existing\n" +
            "                      dev/test database that already has other data in it.\n" +
            "  --dry-run           Read and report row counts from --source only; writes nothing to SpacetimeDB\n" +
            "                      (--target-db/--target-host aren't required with this).\n" +
            "  --continue-on-error Keep going past a row that fails to migrate instead of stopping immediately.\n" +
            "  --limit N           Only migrate the first N rows of each entity - for testing against a small\n" +
            "                      subset before running the full migration.";

        public static MigrationOptions Parse(string[] args)
        {
            var options = new MigrationOptions();

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--source":
                        options.SourceSqlitePath = RequireValue(args, ref i, "--source");
                        break;
                    case "--source-log":
                        options.SourceLogSqlitePath = RequireValue(args, ref i, "--source-log");
                        break;
                    case "--target-host":
                        options.TargetHost = RequireValue(args, ref i, "--target-host");
                        break;
                    case "--target-db":
                        options.TargetDatabase = RequireValue(args, ref i, "--target-db");
                        break;
                    case "--dry-run":
                        options.DryRun = true;
                        break;
                    case "--continue-on-error":
                        options.ContinueOnError = true;
                        break;
                    case "--limit":
                        options.Limit = int.Parse(RequireValue(args, ref i, "--limit"));
                        break;
                    case "--help":
                    case "-h":
                        throw new ArgumentException(Usage);
                    default:
                        throw new ArgumentException($"Unrecognized argument: {args[i]}");
                }
            }

            if (string.IsNullOrEmpty(options.SourceSqlitePath))
            {
                throw new ArgumentException("--source is required.");
            }

            if (!options.DryRun && string.IsNullOrEmpty(options.TargetDatabase))
            {
                throw new ArgumentException("--target-db is required unless --dry-run is set.");
            }

            return options;
        }

        private static string RequireValue(string[] args, ref int i, string flag)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"{flag} requires a value.");
            }

            return args[++i];
        }
    }
}
