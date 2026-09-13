# Sonarr.SpacetimeMigration

One-time tool for cutting an existing SQLite-backed Sonarr install over to SpacetimeDB.

Covers every entity this port has ported except:

- **`Command`** - not migrated at all, and the tool actively refuses to run if the source has any
  command still `Queued` or `Started` (see `RequireNoPendingCommands` in `Program.cs`). This isn't
  the same "safe to skip" case as the entities below: a normal Sonarr restart reloads every
  `Queued` command from the database (`CommandQueueManager.Requeue()`), so a command genuinely in
  flight at migration time would be silently lost, not recreated, if this tool just skipped the
  table quietly. Let the source instance's queue fully drain (check its Activity/Queue page)
  before migrating.
- **`ImportListItem`, `SceneMapping`, `QualityProfileQualityRank`** and the four provider status
  tables (`IndexerStatus`/`DownloadClientStatus`/`ImportListStatus`/`NotificationStatus`) - the
  same entities the Spacetime housekeeping-task audit left SQL-only, for the same reason:
  ephemeral or fully recreatable state (cached list items, cached scene mappings, a derived
  quality-rank cache seeded automatically at the next app startup, transient health/backoff
  tracking), never something a migration needs to carry over.

**`UpdateHistory`** is migrated only when `--source-log` is given, unlike everything else here: it
lives in the source's separate `logs.db` file (its own real repository is constructed with
`ILogDatabase`, not `IMainDatabase`), and dropping it is a genuine feature regression (previous-
version detection, installed-update annotations), not a cost-free one - so it's deliberately
opt-in rather than silently skipped. Take a `.backup` snapshot of `logs.db` the same way as
`sonarr.db` (see below) if you want it preserved.

Everything else - `CustomFormat`, `Config`, `NamingConfig`, `RootFolder`, `RemotePathMapping`,
`CustomFilter`, `DelayProfile`, `ReleaseProfile`, `IndexerDefinition`, `DownloadClientDefinition`,
`ImportListDefinition`, `NotificationDefinition`, `MetadataDefinition`, `ImportListExclusion`,
`QualityDefinition`, `AutoTag`, `User`, `PendingRelease`, `DownloadHistory`, `MetadataFile`,
`SubtitleFile`, `OtherExtraFile`, `ScheduledTask`, plus the original core entities (`Tag`,
`QualityProfile`, `Series` including tags, `EpisodeFile`, `Episode`, `EpisodeHistory`,
`Blocklist`) - is migrated unconditionally. Extend `Program.cs` with the same pattern (a
`MigrateInsert` reducer + repository override, a source read, a `MigrateEntity` call) if a new
entity is ported later.

## How it works

Reads through the **real**, unmodified SQL-backed repository class for every migrated entity
(`TagRepository`, `QualityProfileRepository`, `SeriesRepository`, `EpisodeRepository`,
`IndexerRepository`, `UserRepository`, and so on - see `Program.cs` for the full list) opened
read-only against a SQLite file - this guarantees correct interpretation of every custom
JSON/int-converter shape already in the source data (Quality/Language int-only storage expanded
to full objects, CustomFormat ids rehydrated, etc.) for free, the same way the real running app
already does it every day.

Writes through each `SpacetimeXRepository`'s `MigrateInsert` method - a near-verbatim copy of that
repository's own `InvokeInsertReducer`, just preserving the row's original id instead of always
assigning a fresh auto-incremented one, and calling a `MigrateInsert<Entity>` reducer instead of
the normal `insert_<entity>` one. Preserving ids means every foreign key (`Episode.SeriesId`,
`EpisodeFile` references, etc.) transfers as-is - no in-script id-remapping table needed.

Deliberately contains no bespoke JSON-shape or Quality/Language lookup logic of its own for
exactly that reason - duplicating either side's serialization here would be one more place for
them to silently drift apart. (This mattered in practice: `QualityProfile.Items` and
`.FormatItems`, two JSON fields on the same entity, are stored in genuinely different shapes even
on the real SQL side.)

## Before running

**Take a consistent, read-only snapshot of the source database - never point `--source` at the
live `sonarr.db` file directly.** Sonarr's SQLite database runs in WAL mode; a naive file copy of
a live database can miss data still sitting in the `.db-wal` file. SQLite's own `.backup` command
produces a correct, point-in-time-consistent copy even while the source instance keeps running:

```bash
# On the host running the real Sonarr instance:
sqlite3 /path/to/sonarr.db ".backup /tmp/sonarr-migration-snapshot.db"
# Copy /tmp/sonarr-migration-snapshot.db to wherever you're running this tool, then delete
# the temp copy on the source host.
```

`--source` is opened `Mode=ReadOnly` regardless - this tool cannot write to the file it reads
from under any code path - but a live file is still the wrong thing to point it at.

**Stop (or fully quiesce) the source Sonarr instance before taking the snapshot you'll actually
cut over with.** `RequireNoPendingCommands` (see below) only ever sees the snapshot, not the live
instance - it can't detect a command that gets queued, or that finishes, in the gap between when
you take the snapshot and when you actually run the migration. Taking a snapshot while Sonarr
keeps running is fine for a dry run or a rehearsal, but for the real cutover, stop Sonarr (or at
minimum confirm its Activity/Queue page is empty) immediately before the snapshot you'll migrate
from, so "no pending commands in the snapshot" actually means "no pending commands, period."

**The target SpacetimeDB database must be genuinely fresh and empty.** This tool verifies every
table it's about to write to is empty before writing anything (`RequireEmptyTarget` in
`Program.cs` - `UpdateHistory` included only when `--source-log` is given) and refuses to proceed
otherwise. This isn't just caution: row-count-based write verification (see below) can't tell
"this database already had N rows before we started" apart from "our N inserts landed" -
re-running against an already-populated database could otherwise report false success while every
insert actually failed on a duplicate primary key.

**The source instance's command queue must be empty.** This tool refuses to proceed if the source
has any `Command` row still `Queued` or `Started` (`RequireNoPendingCommands` in `Program.cs`) -
see the `Command` entry above for why.

## Usage

```bash
dotnet run --project Sonarr.SpacetimeMigration -- \
  --source /path/to/sonarr-migration-snapshot.db \
  --source-log /path/to/logs-migration-snapshot.db \
  --target-host http://127.0.0.1:3000 \
  --target-db sonarr-spacetime-production
```

- `--source-log` - optional; a `.backup` snapshot of `logs.db` taken the same way as `--source`.
  Only needed to migrate `UpdateHistory` - omit to skip it.
- `--dry-run` - read and report row counts from `--source` only; writes nothing to SpacetimeDB
  (`--target-db`/`--target-host` aren't required with this). Always run this first to sanity-check
  the source before touching any target.
- `--limit N` - only migrate the first N rows of each entity, for testing against a small subset.
- `--continue-on-error` - keep going past a row that fails to migrate instead of stopping
  immediately.

## Write verification

Reducer calls over the SpacetimeDB C# SDK are fire-and-forget from the caller's point of view -
they queue the request over the WebSocket and return immediately, with the actual write (and its
visibility to a subsequent query) happening asynchronously once the connection's background
FrameTick pump processes the server's response. A tight loop of thousands of these can out-run
that pump - confirmed empirically the first time this tool was run against a real 76MB production
snapshot, where every entity *reported* zero failures but the last two entities processed
(History, Blocklist) were nowhere close to fully written by the time the process disposed its
connection and exited.

Because of this, every write path in this tool (each entity's `MigrateInsert` loop, plus the
`ReplaceSeriesTags` calls during Series migration) is followed by polling the target's actual
server-side row count until it matches what was sent, with a 5-minute deadline. If it doesn't
converge, the tool throws rather than reporting success - never trust a run that didn't reach
"migrated and confirmed in the target database" for every entity.
