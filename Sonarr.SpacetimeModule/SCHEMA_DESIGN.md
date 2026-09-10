# Phase 3: Schema & query-strategy design

Source of truth for the target SpacetimeDB schema and, per entity, how each one's real query
patterns map onto SpacetimeDB's actual query surface.

**Revision note:** this is v2. v1 was adversarially reviewed by Codex (full transcript in the
session, not checked into the repo) before Phase 4 started, specifically to catch wrong
assumptions before a large repository port got built against them. That review found real,
substantive problems — not nitpicks — described inline below wherever they change a decision.
The single biggest one: v1's survey scope (all `BasicRepository<T>` subclasses) missed
`StatisticsRepository` and `SeriesStatisticsRepository`, neither of which inherits
`BasicRepository<T>`, both of which run `SUM`/`MIN`/`MAX`/`GROUP BY` over tables v1 had
classified as trivial. That's now its own section below, not a footnote.

## Query language capabilities (unchanged from v1, not disputed by review)

| Feature | Subscriptions | Ad-hoc (`RemoteQuery`) |
|---|---|---|
| `=`, `<`, `>`, `<=`, `>=`, `!=` | Yes | Yes |
| `AND` / `OR` | Yes | Yes |
| `BETWEEN`, `IN`, `LIKE`, `IS NULL`, `NOT` | **No** | **No** |
| `JOIN` | Up to 2 tables, indexed join columns | Unrestricted multi-table |
| `ORDER BY` / `LIMIT` | **No** | Yes |
| `COUNT` | Yes | Yes |
| `SUM`, `MAX`, `MIN`, `GROUP BY` | **No** | **No** |

`WHERE x IN (a, b, c)` rewrites as `x = a OR x = b OR x = c` for small id lists.  `RemoteQuery`
supports real joins and `ORDER BY`/`LIMIT`, so genuine multi-table *reads* don't automatically
need denormalization. What's unportable is aggregate/computed logic on top of those joins.

## Structural finding: Queue isn't a table — but the v1 dataflow description was wrong

Schema claim (correct, confirmed by review): `src/NzbDrone.Core/Queue/` has no
`BasicRepository` subclass and no entry in `TableMapping.cs`. There is nothing to schema-design
for Queue itself.

**v1 said** `QueueService` computes the queue from `PendingRelease` rows plus live
download-client polling. **That's wrong.** The real dataflow, per the review:
- `QueueService` only holds a static in-memory list, replaced wholesale whenever it receives a
  `TrackedDownloadRefreshedEvent` (`QueueService.cs:21`, `:96`) — live downloads only, nothing
  about `PendingRelease`.
- `PendingReleaseService` independently reloads `PendingRelease` rows into its *own* static list
  (`PendingReleaseService.cs:644`) and reconstructs each one's `RemoteEpisode` via Series,
  parsing, augmentation, and custom-format services (`:337`) before mapping them into queue-item
  shape (`:194`).
- The API concatenates both independently-produced lists (`V5 QueueController.cs:173`, `:185`).

**Decision, corrected:** porting `PendingRelease` (Tier 1, below) is still the right and only
schema work for Queue — there is no missing table. But "leave `QueueService` untouched" is not a
complete dataflow specification; `PendingReleaseService`'s reconstruction logic is the piece that
actually needs to keep working against the ported `PendingRelease` table, and it's business logic
sitting on top of the port, not something Phase 3 needs to redesign. Flag this precisely for
whoever implements Phase 4/6 so "port PendingRelease" isn't mistaken for "port Queue."

The `PendingRelease` repository's `InnerJoin` to Series is confirmed genuinely vestigial by the
review (`WithoutFallback()` has no caller; the real reload path uses `.All()`) — drop it rather
than port it, as v1 already recommended.

## Tier 1: trivial — single-table WHERE, port as-is

RootFolder, ScheduledTask, SceneMapping, DownloadHistory, CustomFormat, Tag (already ported),
AutoTag, CustomFilter, User, Config, ExtraFile, ImportListExclusion, ImportListItem, NamingConfig,
DelayProfile, QualityProfileQualityRank (storage only — see the replace-set reducer requirement
below), ReleaseProfile, QualityDefinition, RemotePathMapping, ProviderStatus
(Indexer/DownloadClient/ImportList/Notification status — 4 entities), Command (bulk raw-SQL
UPDATE, single-table), SubtitleFile, MetadataFile (storage only — see Housekeeping section for
its non-trivial cleanup queries), MetadataDefinition.

**Two v1 entries corrected here, not trivial as originally stated:**

- **EpisodeFile** — v1 filed this as direct-translation Tier 1. It isn't: `MediaFileRepository
  .GetFilesWithoutMediaInfo` is exactly an `IS NULL` predicate (`MediaFileRepository.cs:40`),
  which the capability table above explicitly marks unsupported. EpisodeFile's *storage* is still
  Tier 1 (no joins on its main read paths) — decision: this one query becomes fetch-all +
  client-side `MediaInfo == null` filter, acceptable because EpisodeFile row counts are bounded
  by library size (thousands, not millions) and this is a maintenance-shaped query, not a
  hot path. If it turns out to be called more often than that assumption holds, revisit with a
  denormalized `HasMediaInfo bool` column instead.
- **UpdateHistory** — v1 filed this as Tier 1 *and* separately said only `Log` is a LogDatabase
  entity being dropped. Both can't be true: `UpdateHistoryRepository` is constructed with
  `ILogDatabase`, exactly like `LogRepository` (`UpdateHistoryRepository.cs:16` vs.
  `LogRepository.cs:12`) — it's a LogDatabase entity too. Update history is read by update APIs
  and written at startup (`UpdateHistoryService.cs:34`, `:62`), so silently dropping it with
  LogDatabase is a real feature regression (previous-version detection, installed-update
  annotations), not a cost-free simplification like `Log` itself is. **Decision: port
  UpdateHistory as an ordinary MainDatabase-equivalent Tier 1 table**, not alongside `Log`. Its
  write volume is occasional (once per update), nothing like `Log`'s.

`PendingRelease` — join is vestigial (see above), otherwise Tier 1.

## Tier 1.5: trivial schema, needs a client-side reshape step (not a query problem)

**QualityProfile** — `QualityProfileRepository` does no SQL joins; every read does an
application-level join already (cross-references `ICustomFormatService.All()`, already in-memory
today) to hydrate `FormatItems[].Format`. Translates directly: `RemoteQuery` the profile row, keep
a client-side `Dictionary<int, CustomFormat>` fed by CustomFormat's subscription.

**ProviderRepository&lt;T&gt;** (Indexer/ImportList/Notification/DownloadClient *definitions*) —
`Settings`'s .NET type depends on `ConfigContract`, resolved via reflection. Single-table storage
is fine; deserialization moves client-side after `RemoteQuery`.

**SeriesRepository**'s `AllSeriesTvdbIds`/`Paths`/`Tags`/`QualityProfiles` — simple single-table
projections into `Dictionary<int, T>` shapes. Trivial reshape.

**New in v2 — `SeriesRepository.FindByTitleInexact`.** Missed by v1's `BasicRepository<T>`-scoped
survey the same way the Statistics repositories were: it uses SQLite `instr`/PostgreSQL `strpos`
(`SeriesRepository.cs:56`), neither in SpacetimeDB's WHERE grammar, and it's a live fallback
during release parsing (`ParsingService.cs:337`) — not dead code. Decision: this cannot be a
per-call `RemoteQuery`; maintain an in-memory `List<(int Id, string Title)>` cache fed by the
Series subscription (series counts are realistically bounded — hundreds, not millions — so
holding titles in memory is cheap) and do the substring match in C#. Refresh the cache on
`Series` insert/update/delete callbacks rather than re-fetching per call.

## Tier 2: real design decisions — Episode, EpisodeHistory, Blocklist

These three share the `QualityProfileQualityRanks` sort/cutoff pattern. **v1 got the actual
semantics of this pattern wrong** — corrected below — in addition to the join/JSON-extraction
translation problem it correctly identified.

### The quality-rank lookup: what v1 got wrong, and the real fix

v1 proposed adding a real `QualityId int` column next to the embedded `Quality` document, then
computing the cutoff score as `MAX(Score)` over the profile's rank rows. **The `MAX` is wrong.**
Per `QualityProfileRankService.cs:32` and `:120`, the real operation is a **keyed lookup by
`(ProfileId, QualityId)`, defaulting to `-1` if absent** — not a maximum over the profile. `MAX`
appears in the existing SQL only because those queries also `GROUP BY` for an unrelated reason,
not because "the largest rank in the profile" is ever the desired value. Corrected decision:
`RemoteQuery` `WHERE ProfileId = x AND QualityId = y` (a two-column equality lookup, not an
aggregate) against the small per-profile rank set, defaulting to `-1` client-side if no row comes
back. This is simpler than v1's version, not just more correct — no `MAX` needed at all.

**The `QualityId` column itself needs a write-time invariant v1 never defined.** The review traced
every write path that touches `EpisodeFile.Quality`/`EpisodeHistory`/`Blocklist` and confirmed the
domain models have no such field today (`EpisodeFile.cs:13`, `EpisodeHistory.cs:24`,
`Blocklist.cs:14`, `QualityModel.cs:8`) — so nothing today keeps a hypothetical scalar in sync
with the embedded document, and several independent call sites mutate `Quality`:
- EpisodeFile: constructed from `LocalEpisode` on import (`ImportApprovedEpisodes.cs:87`, `:149`,
  `MediaFileService.cs:43`) **and** explicitly replaced by both REST API generations
  (`V3/V5 EpisodeFileController.cs`, `MediaFileService.cs:50`) — this one is genuinely mutable
  post-insert, not insert-only.
- EpisodeHistory: multiple independent constructors (grab, delete, rename, bulk-ignored — all in
  `HistoryService.cs`), each building its own row.
- Blocklist: at least two independent constructors (`BlocklistService.cs:75`, `:126`).

The review found no current code path that mutates `Quality` *without* going through one of these
choke points, so a single derivation point per entity is achievable — but it must be enforced,
not assumed. **Decision:** the `insert_<entity>`/`update_<entity>` reducers for EpisodeFile,
EpisodeHistory, and Blocklist must derive `QualityId` from the submitted `Quality` document
*inside the reducer*, and must reject (or ignore) any independently-submitted `QualityId` from the
caller — there is exactly one place `QualityId` is allowed to originate, the reducer's own
parsing of `Quality`. This is the concrete failure mode the review named: without this
enforcement, a V5 bulk quality change updates the embedded document but the reducer could
silently accept a stale or mismatched `QualityId` passed alongside it, and the row then sorts and
filters under the wrong quality. Existing-row backfill during the Phase 4/6 data migration must
use the same derivation logic, not a separate one-off script.

### Pagination: the coarse-fetch approach breaks `GetPaged`, corrected

**Episode-specific (`EpisodeRepository`):**
- `PagedQuery`/`EpisodesWithFiles`: genuine joins (Episode↔Series, Episode↔EpisodeFile) —
  `RemoteQuery` joins or client-side correlation both work, no denormalization needed. v1 was
  right here, unchanged.
- `SetMonitored`/`SetMonitoredBySeason`: bulk raw-SQL UPDATE from enum-keyed predicate fragments —
  becomes a small set of dedicated reducers, mechanical. Unchanged from v1.
- `EpisodesWithoutFiles` (the "missing episodes" query): **v1's "fetch coarse, refine
  client-side" plan is incorrect as stated.** The review's concrete finding: `GetPagedRecords`
  applies filtering, `ORDER BY`, `LIMIT`, and `OFFSET` together in one SQL statement
  (`BasicRepository.cs:450`), with `TotalRecords` from a separately-filtered count query (`:475`).
  If the air-date-cutoff refinement (`AirDateUtc + Series.Runtime <= now`, real date arithmetic
  SpacetimeDB's WHERE can't express) happens *after* a coarse `RemoteQuery` that already applied
  `LIMIT`/`OFFSET`, a false-positive coarse row can consume a page slot, producing a short page,
  shifting every later row's offset, and an inflated `TotalRecords` — the coarse fetch and the
  true page are not the same set. The worst case isn't small either: the coarse predicate alone
  (`EpisodeFileId == 0`, no other filters) can match every historical episode without a file in
  the library, and the existing bulk-search path already defines its own upper bound at
  1,000,000 rows for the equivalent unpaginated case (`EpisodeSearchService.cs:137`).
  **Corrected decision:** for this specific query, do not attempt server-side pagination against
  the coarse set at all. Fetch the *entire* coarse candidate set via `RemoteQuery`, apply every
  refinement (date arithmetic, tag membership) in C#, then paginate the fully-refined in-memory
  list. This trades "SQL does the heavy lifting" for "the app does," which costs more per request
  at large scale than today's single indexed SQL query — an honest regression, not a free
  translation. It's acceptable at Sonarr's actual target scale (a personal/small-group media
  library, realistically low thousands of episodes) but should be flagged explicitly to the user
  before Phase 6 ships it, not discovered later as a performance surprise. If it becomes a real
  problem, the fix is a maintained `IsCutoffMet`/`IsAvailable`-shaped denormalized boolean kept
  current on every relevant write (same pattern as `QualityId` above), not more clever client-side
  filtering.
- Series.Tags-array-contains filtering: v1's proposed fix — replace the embedded tag array with a
  `SeriesTag(SeriesId, TagId)` junction table, turning the array-contains hack into a plain
  equality WHERE — still stands and is unaffected by the pagination finding (it composes into the
  same coarse-fetch-then-refine step above). **What v1 omitted, per the review:** the junction
  table's *write* semantics. Series tags change via at least three different call shapes today —
  full single-series update (`SeriesService.cs:224`), full bulk update (`:237`), and a
  tags-only `SetFields` (`:314`) — plus the API editor's add/remove/replace mutation of the
  `HashSet<int>` (`V5 SeriesEditorController.cs:78`). **Decision:** a dedicated
  `replace_series_tags(seriesId, tagIds)` reducer that atomically deletes all existing
  `SeriesTag` rows for that series and inserts the new set in one transactional reducer call —
  not independent generic delete/insert calls, which would expose a transiently empty or
  partially-replaced tag set to any concurrent reader/subscriber.

**EpisodeHistory-specific (`HistoryRepository`):** `GetBySeries`/`GetBySeason`/`GetByEpisode`/
`Since` joins are genuine reads, same treatment as Episode's joins, unaffected by the pagination
finding above (none of these are paginated the same way `EpisodesWithoutFiles` is — confirm this
per call site before assuming it's safe, but nothing in the review contradicts it). Language/
quality `LIKE`-on-serialized-JSON filtering: same junction-table-or-client-filter normalization
call as Series.Tags, same write-semantics requirement (a dedicated replace-set reducer, not
generic CRUD, if a junction table is chosen). `MostRecentForEpisode`/`MostRecentForDownloadId`
are genuinely trivial (`.MaxBy` over an already-fetched in-memory list in the *current* code too)
— unchanged from v1.

**Blocklist (`BlocklistRepository`):** same `QualityProfileQualityRanks` fix as above (keyed
lookup, not `MAX`), same quality-rank write-invariant requirement. `BlocklistedByTitle`/
`BlocklistedByTorrentInfoHash` use SQL substring `LIKE` — confirm real call sites are exact-match
in practice before deciding an equality WHERE suffices vs. needing the same client-side
substring-filter treatment as `FindByTitleInexact` above.

## New in v2: aggregate/statistics endpoints outside the repository survey

**This is the review's most important finding.** v1's survey scope was every `BasicRepository<T>`
subclass. `StatisticsRepository` and `SeriesStatisticsRepository` don't inherit it, so they were
invisible to that survey — and both run exactly the aggregate operations
(`SUM`/`MIN`/`MAX`/`GROUP BY`) the capability table at the top of this document says SpacetimeDB
cannot do, over the same Episode/EpisodeFile/Series/QualityProfile/Tag tables v1 called trivial.

- `StatisticsRepository`: eleven queries (`StatisticsRepository.cs:32` onward) including Episode
  conditional sums joined to Series (`:325`), EpisodeFile `SUM(Size)` (`:349`), QualityProfile/
  Series grouping (`:364`), a Tag/Series JSON join with grouping (`:400`), EpisodeFile→Series→Tag
  aggregation (`:426`), and quality JSON extraction plus grouping and size sum (`:444`). Live on
  the V5 API (`StatisticsController.cs:21`).
- `SeriesStatisticsRepository`: Episode `COUNT`/`SUM`/`MIN`/`MAX`/`GROUP BY` (`:69`) and
  EpisodeFile `SUM`/`GROUP_CONCAT`(SQLite)/`string_agg`(Postgres)/`GROUP BY` (`:93`) — fetched on
  every normal series-list and series-detail read (`V5 SeriesController.cs:112`, `:280`), not a
  rarely-used reporting path.

**Decision:** none of this can be pushed into SpacetimeDB SQL at all — no aggregate functions
exist in either the subscription or ad-hoc query grammar. These become pure client-side
computations: `RemoteQuery`/subscribe to the underlying tables (Episode, EpisodeFile, Series,
QualityProfile, Tag — all already being ported for other reasons) and compute the sums/counts/
min/max/grouping in C# over the fetched rows, the same reshape pattern already established for
QualityProfile's application-level join. Because `SeriesStatisticsRepository` runs on every
series list/detail view, this is not a low-traffic path — cache the computed statistics
per-series and invalidate on the relevant table's write callbacks rather than recomputing from a
full fetch on every request. This needs its own implementation task in Phase 4/6; it is not
covered by porting the entity repositories themselves, and a port that stops at the
`BasicRepository<T>` boundary will compile and appear complete while these endpoints fail at
runtime against a database that no longer exists.

## New in v2: housekeeping tasks with raw multi-table queries

Also missed by the `BasicRepository<T>`-scoped survey, also not migration-only SQL —
`HousekeepingService` (`HousekeepingService.cs:22`) runs these as live scheduled tasks:
- `CleanupDuplicateMetadataFiles`: `MIN`/`GROUP BY`/`HAVING COUNT`/subquery/delete
  (`CleanupDuplicateMetadataFiles.cs:15`) — over `MetadataFile`, filed as Tier 1 storage above.
- `CleanupOrphanedEpisodeFiles`/`CleanupOrphanedMetadataFiles`/`CleanupOrphanedExtraFiles`/
  `CleanupOrphanedSubtitleFiles`/`CleanupOrphanedPendingReleases`: each a multi-table delete
  query (child table joined to Series/EpisodeFile to find rows whose parent no longer exists).

**Decision:** these are maintenance jobs, not hot user-facing paths — latency tolerance is much
higher than anywhere else in this document. Reimplement each as: `RemoteQuery` the child table
and the parent table(s) it depends on, compute the orphan/duplicate set in C# (a plain set
difference or grouping, trivial at maintenance-job cadence), then call the standard
`delete_<entity>` reducer per row found. No new reducers needed here, unlike the quality-rank and
tag-replacement cases above — this is a case where "fetch broad, compute client-side, delete via
existing primitives" is the right shape precisely because it runs on a schedule, not per-request.

## LogDatabase: not ported — narrowed to just `Log` in v2 (see UpdateHistory correction above)

`Log` (table `Logs`) is the only entity actually staying dropped. High write volume, zero
relational complexity, no query patterns worth preserving — cut over to structured container
logging (podman/journald already captures this) instead of porting. `UpdateHistory` is *not*
grouped with this decision in v2 — see the Tier 1 correction above.

## Generic reducer convention — corrected to name the exceptions explicitly

Per entity, the module exposes `insert_<entity>`/`update_<entity>`/`delete_<entity>`, following
`SpacetimeTagRepository`'s established pattern, **except** where this document calls for
something else specifically:
- EpisodeFile/EpisodeHistory/Blocklist's `insert`/`update` reducers must derive `QualityId` from
  the submitted `Quality` document internally (see above) — not a plain field-copy like Tag's.
- `QualityProfileQualityRanks` needs a dedicated `replace_quality_profile_ranks(profileId, ranks)`
  reducer doing an atomic delete-all-then-insert-set in one transaction — the existing SQL
  repository already requires this same replace-whole-set semantic
  (`QualityProfileRankRepository.cs:21`), so this isn't a new requirement invented by the port,
  just one that generic per-row CRUD reducers can't satisfy safely (independent delete-then-insert
  calls expose a transiently empty or partial rank set to concurrent readers).
- The proposed `SeriesTag` junction needs the equivalent `replace_series_tags(seriesId, tagIds)`
  reducer, same reasoning.
- Episode's monitor-status methods become a small set of purpose-built reducers (unchanged from
  v1).

Reads stay entirely client-side (`RemoteQuery`, following `SpacetimeTagRepository`'s
lock-and-requery-on-write pattern) — no "read reducers" anywhere in this design.

## Exit criteria — recount corrected (v1's numbers didn't add up)

v1 claimed "Tier 1 (25), Tier 1.5 (3), Tier 2 (3)" — the review confirmed this counted conceptual
bullets, not the 40 actual table registrations in `TableMapping.cs:61`. Corrected count against
the real registrations:
- **Tier 1: 30 tables** (including the corrected EpisodeFile/UpdateHistory entries and the 4
  ProviderStatus entities counted individually).
- **Tier 1.5: 6 tables** (QualityProfile, Series, and the 4 ProviderRepository-based
  definition entities — Indexer/ImportList/Notification/DownloadClient).
- **Tier 2: 3 tables** (Episode, EpisodeHistory, Blocklist).
- **Log: 1 table**, not ported.
- Total: 40, matching `TableMapping.cs`.

Additional exit criteria, new in v2:
- Aggregate/statistics endpoints (StatisticsRepository, SeriesStatisticsRepository) have an
  explicit client-side-computation decision and are called out as their own Phase 4/6
  implementation task, not assumed covered by the entity ports.
- Housekeeping tasks with raw multi-table queries have an explicit fetch-and-compute decision.
- `QualityId` write-time derivation and the two replace-set reducers (`QualityProfileQualityRanks`,
  `SeriesTag`) are named as required reducers, not left to generic CRUD.
- The pagination-correctness problem in `EpisodesWithoutFiles` has an explicit (if imperfect)
  resolution, flagged as a scale tradeoff to surface to the user rather than a silent regression.
