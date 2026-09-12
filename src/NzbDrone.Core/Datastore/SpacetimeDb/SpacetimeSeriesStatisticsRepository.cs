using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    // StatisticsRepository/SeriesStatisticsRepository don't inherit SpacetimeBasicRepository<T> -
    // they were out of scope for the entity-by-entity Tier 1/1.5/2 port and are their own Phase 3
    // schema-design task (see SCHEMA_DESIGN.md's "aggregate/statistics endpoints" section): the
    // real repository's SUM/MIN/MAX/GROUP BY has no SpacetimeDB SQL equivalent (neither the
    // subscription nor ad-hoc query grammar support aggregates at all), so this becomes pure
    // client-side computation over the already-ported Episode/EpisodeFile repositories instead of
    // a query translation. This is a hot path - SeriesController hits it on every normal series
    // list/detail view, not a rare reporting page - so unlike most of this port's "fetch
    // everything, compute in C#" tradeoffs, this one is a real, not-yet-addressed regression risk
    // at larger library sizes; the schema design doc calls out per-series caching invalidated on
    // Episode/EpisodeFile writes as the fix if this becomes a real problem, not attempted here.
    public class SpacetimeSeriesStatisticsRepository : ISeriesStatisticsRepository
    {
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IMediaFileRepository _mediaFileRepository;

        public SpacetimeSeriesStatisticsRepository(IEpisodeRepository episodeRepository, IMediaFileRepository mediaFileRepository)
        {
            _episodeRepository = episodeRepository;
            _mediaFileRepository = mediaFileRepository;
        }

        public List<SeasonStatistics> SeriesStatistics()
        {
            var now = DateTime.UtcNow;
            return Compute(_episodeRepository.All(), _mediaFileRepository.All(), now);
        }

        public List<SeasonStatistics> SeriesStatistics(int seriesId)
        {
            var now = DateTime.UtcNow;
            return Compute(_episodeRepository.GetEpisodes(seriesId), _mediaFileRepository.GetFilesBySeries(seriesId), now);
        }

        // now must be captured before the fetches below, not after (matching the real
        // repository's EpisodesBuilder, which binds @currentDate before the query runs) - the
        // fetches are two separate RemoteQuery network round-trips, not a single atomic read,
        // so capturing it afterward would risk classifying an episode that airs mid-fetch
        // differently than the real SQL would (next-airing/unavailable vs previous-airing/
        // available).
        private static List<SeasonStatistics> Compute(IEnumerable<Episode> episodes, IEnumerable<EpisodeFile> files, DateTime now)
        {
            var filesBySeason = files
                .GroupBy(f => (f.SeriesId, f.SeasonNumber))
                .ToDictionary(g => g.Key, g => g.ToList());

            return episodes
                .GroupBy(e => (e.SeriesId, e.SeasonNumber))
                .Select(g => BuildSeasonStatistics(g.Key.SeriesId, g.Key.SeasonNumber, g.ToList(), filesBySeason.GetValueOrDefault(g.Key), now))
                .ToList();
        }

        // Mirrors SeriesStatisticsRepository.EpisodesBuilder/EpisodeFilesBuilder's SQL exactly,
        // predicate for predicate - see that file for the real CASE expressions this replicates.
        private static SeasonStatistics BuildSeasonStatistics(int seriesId, int seasonNumber, List<Episode> seasonEpisodes, List<EpisodeFile> seasonFiles, DateTime now)
        {
            var stats = new SeasonStatistics
            {
                SeriesId = seriesId,
                SeasonNumber = seasonNumber,
                TotalEpisodeCount = seasonEpisodes.Count,
                AvailableEpisodeCount = seasonEpisodes.Count(e => (e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now) || e.EpisodeFileId > 0),
                EpisodeCount = seasonEpisodes.Count(e => (e.Monitored && e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now) || e.EpisodeFileId > 0),
                EpisodeFileCount = seasonEpisodes.Count(e => e.EpisodeFileId > 0),
                MonitoredEpisodeCount = seasonEpisodes.Count(e => e.Monitored),
                NextAiringString = seasonEpisodes
                    .Where(e => e.Monitored && e.AirDateUtc.HasValue && e.AirDateUtc.Value >= now)
                    .Select(e => (DateTime?)e.AirDateUtc.Value)
                    .DefaultIfEmpty()
                    .Min()?.ToString("o"),
                PreviousAiringString = seasonEpisodes
                    .Where(e => e.Monitored && e.AirDateUtc.HasValue && e.AirDateUtc.Value < now)
                    .Select(e => (DateTime?)e.AirDateUtc.Value)
                    .DefaultIfEmpty()
                    .Max()?.ToString("o"),
                LastAiredString = seasonEpisodes
                    .Select(e => e.AirDate)
                    .Where(a => !string.IsNullOrEmpty(a))
                    .DefaultIfEmpty()
                    .Max()
            };

            if (seasonFiles is { Count: > 0 })
            {
                stats.SizeOnDisk = seasonFiles.Sum(f => f.Size);
                stats.ReleaseGroupsString = string.Join('|', seasonFiles.Select(f => f.ReleaseGroup).Where(g => !string.IsNullOrEmpty(g)).Distinct());
                stats.ReleaseTypesString = string.Join('|', seasonFiles.Select(f => (int)f.ReleaseType).Distinct());
                stats.EpisodeFileQualitiesString = string.Join('|', seasonFiles.Select(f => f.Quality?.Quality?.Id).Where(id => id.HasValue).Distinct());
            }

            return stats;
        }
    }
}
