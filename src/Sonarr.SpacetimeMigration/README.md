# Sonarr.SpacetimeMigration

One-time tool for cutting an existing SQLite-backed Sonarr install over to SpacetimeDB.

First-pass scope: `Tag`, `QualityProfile`, `Series` (including tags), `EpisodeFile`, `Episode`,
`EpisodeHistory`, `Blocklist` - the "core" entities. Everything else this port has ported is not
yet covered; extend `Program.cs` with the same pattern (a `MigrateInsert` reducer + repository
override, a source read, a `MigrateEntity` call) as needed.

## How it works

Reads through the **real**, unmodified SQL-backed repository classes (`TagRepository`,
`QualityProfileRepository`, `SeriesRepository`, `EpisodeRepository`, `MediaFileRepository`,
`HistoryRepository`, `BlocklistRepository`) opened read-only against a SQLite file - this
guarantees correct interpretation of every custom JSON/int-converter shape already in the source
data (Quality/Language int-only storage expanded to full objects, CustomFormat ids rehydrated,
etc.) for free, the same way the real running app already does it every day.

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

**The target SpacetimeDB database must be genuinely fresh and empty.** This tool verifies every
target table is empty before writing anything (`RequireEmptyTarget` in `Program.cs`) and refuses
to proceed otherwise. This isn't just caution: row-count-based write verification (see below)
can't tell "this database already had N rows before we started" apart from "our N inserts
landed" - re-running against an already-populated database could otherwise report false success
while every insert actually failed on a duplicate primary key.

## Usage

```bash
dotnet run --project Sonarr.SpacetimeMigration -- \
  --source /path/to/sonarr-migration-snapshot.db \
  --target-host http://127.0.0.1:3000 \
  --target-db sonarr-spacetime-production
```

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
