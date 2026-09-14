using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    // StatisticsRepository doesn't inherit SpacetimeBasicRepository<T> - like
    // SpacetimeSeriesStatisticsRepository, it's its own Phase 3 schema-design task (SUM/MIN/MAX/
    // GROUP BY have no SpacetimeDB SQL equivalent), reimplemented here as client-side LINQ over
    // the already-ported Series/Episode/EpisodeFile/QualityProfile/Tag repositories. This is the
    // V5 statistics page's backing endpoint, not the series list/detail hot path
    // SpacetimeSeriesStatisticsRepository covers - a dedicated, opt-in page, so lower urgency, but
    // left unported it would silently return zeros the same way series stats did before that fix.
    //
    // The real repository's per-builder SQL has a real, pre-existing asymmetry that's faithfully
    // replicated here rather than "fixed": EpisodesBuilder/EpisodeFilesPerProfileBuilder/
    // EpisodeFilesPerTagBuilder always JOIN to Series (so an orphaned Episode/EpisodeFile row -
    // one whose Series has been deleted, which can currently persist since housekeeping cleanup
    // isn't ported either - is always excluded), while CompletedSeriesBuilder/SeasonsBuilder/
    // EpisodeFilesBuilder/EpisodeFilesPerQualityBuilder only join Series when a filter is
    // actually active, running over literally every row (orphans included) when it isn't. See
    // the two episodesForProgress/filesForOverallCount variables below for where this splits.
    public class SpacetimeStatisticsRepository : IStatisticsRepository
    {
        private readonly ISeriesRepository _seriesRepository;
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IQualityProfileRepository _qualityProfileRepository;
        private readonly ITagRepository _tagRepository;

        public SpacetimeStatisticsRepository(
            ISeriesRepository seriesRepository,
            IEpisodeRepository episodeRepository,
            IMediaFileRepository mediaFileRepository,
            IQualityProfileRepository qualityProfileRepository,
            ITagRepository tagRepository)
        {
            _seriesRepository = seriesRepository;
            _episodeRepository = episodeRepository;
            _mediaFileRepository = mediaFileRepository;
            _qualityProfileRepository = qualityProfileRepository;
            _tagRepository = tagRepository;
        }

        public async Task<LibraryStatistics> GetLibraryStatistics(StatisticsFilter filter = null)
        {
            var now = DateTime.UtcNow;
            var hasFilter = HasAnyCondition(filter);
            var seriesPredicate = BuildSeriesPredicate(filter);

            var allSeries = (await _seriesRepository.All()).ToList();
            var matchingSeries = allSeries.Where(seriesPredicate).ToList();
            var matchingSeriesIds = matchingSeries.Select(s => s.Id).ToHashSet();
            var seriesById = matchingSeries.ToDictionary(s => s.Id);

            var allEpisodes = (await _episodeRepository.All()).ToList();
            var matchingEpisodes = allEpisodes.Where(e => matchingSeriesIds.Contains(e.SeriesId)).ToList();
            var episodesForProgress = hasFilter ? matchingEpisodes : allEpisodes;

            var allFiles = (await _mediaFileRepository.All()).ToList();
            var matchingFiles = allFiles.Where(f => matchingSeriesIds.Contains(f.SeriesId)).ToList();
            var filesForOverallCount = hasFilter ? matchingFiles : allFiles;

            bool IsCounted(Episode e) => (e.Monitored && e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now) || e.EpisodeFileId > 0;
            bool HasFile(Episode e) => e.EpisodeFileId > 0;

            var completedSeriesCount = episodesForProgress
                .GroupBy(e => e.SeriesId)
                .Count(g => IsCompletedGroup(g, IsCounted, HasFile));

            var seasonGroups = episodesForProgress
                .Where(e => e.SeasonNumber > 0)
                .GroupBy(e => (e.SeriesId, e.SeasonNumber))
                .ToList();
            var seasonCount = seasonGroups.Count;
            var completedSeasonCount = seasonGroups.Count(g => IsCompletedGroup(g, IsCounted, HasFile));

            var qualityProfileStatistics = await BuildQualityProfileStatistics(matchingSeries, matchingFiles, seriesById);
            var tagStatistics = await BuildTagStatistics(matchingSeries, matchingFiles, seriesById);
            var qualityStatistics = BuildQualityStatistics(filesForOverallCount);

            return new LibraryStatistics
            {
                SeriesCount = matchingSeries.Count,
                MonitoredSeriesCount = matchingSeries.Count(s => s.Monitored),
                CompletedSeriesCount = completedSeriesCount,
                ContinuingSeriesCount = matchingSeries.Count(s => s.Status == SeriesStatusType.Continuing),
                EndedSeriesCount = matchingSeries.Count(s => s.Status == SeriesStatusType.Ended),
                UpcomingSeriesCount = matchingSeries.Count(s => s.Status == SeriesStatusType.Upcoming),
                DeletedSeriesCount = matchingSeries.Count(s => s.Status == SeriesStatusType.Deleted),
                StandardSeriesCount = matchingSeries.Count(s => s.SeriesType == SeriesTypes.Standard),
                DailySeriesCount = matchingSeries.Count(s => s.SeriesType == SeriesTypes.Daily),
                AnimeSeriesCount = matchingSeries.Count(s => s.SeriesType == SeriesTypes.Anime),
                SeasonCount = seasonCount,
                CompletedSeasonCount = completedSeasonCount,
                TotalEpisodeCount = matchingEpisodes.Count,
                MonitoredEpisodeCount = matchingEpisodes.Count(e => e.Monitored),
                DownloadedEpisodeCount = matchingEpisodes.Count(HasFile),
                MissingEpisodeCount = matchingEpisodes.Count(e =>
                    e.EpisodeFileId == 0 && e.Monitored && seriesById[e.SeriesId].Monitored &&
                    e.AirDateUtc.HasValue && e.AirDateUtc.Value <= now),
                UnairedEpisodeCount = matchingEpisodes.Count(e =>
                    e.EpisodeFileId == 0 && (!e.AirDateUtc.HasValue || e.AirDateUtc.Value > now)),
                EpisodeFileCount = filesForOverallCount.Count,
                SizeOnDisk = filesForOverallCount.Sum(f => f.Size),
                QualityProfileStatistics = qualityProfileStatistics,
                QualityStatistics = qualityStatistics,
                TagStatistics = tagStatistics
            };
        }

        private async Task<List<QualityProfileStatistics>> BuildQualityProfileStatistics(List<Series> matchingSeries, List<EpisodeFile> matchingFiles, Dictionary<int, Series> seriesById)
        {
            var seriesCountByProfile = matchingSeries.GroupBy(s => s.QualityProfileId).ToDictionary(g => g.Key, g => g.Count());
            var fileCountsByProfile = matchingFiles
                .GroupBy(f => seriesById[f.SeriesId].QualityProfileId)
                .ToDictionary(g => g.Key, g => (Count: g.Count(), Size: g.Sum(f => f.Size)));

            return (await _qualityProfileRepository.All())
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p =>
                {
                    var files = fileCountsByProfile.GetValueOrDefault(p.Id);
                    return new QualityProfileStatistics
                    {
                        QualityProfileId = p.Id,
                        Name = p.Name,
                        SeriesCount = seriesCountByProfile.GetValueOrDefault(p.Id),
                        EpisodeFileCount = files.Count,
                        SizeOnDisk = files.Size
                    };
                })
                .ToList();
        }

        private async Task<List<TagStatistics>> BuildTagStatistics(List<Series> matchingSeries, List<EpisodeFile> matchingFiles, Dictionary<int, Series> seriesById)
        {
            bool SeriesHasTag(Series series, int tagId) => series.Tags != null && series.Tags.Contains(tagId);

            return (await _tagRepository.All())
                .OrderBy(t => t.Label, StringComparer.Ordinal)
                .Select(t =>
                {
                    var files = matchingFiles.Where(f => SeriesHasTag(seriesById[f.SeriesId], t.Id)).ToList();

                    return new TagStatistics
                    {
                        TagId = t.Id,
                        Label = t.Label,
                        SeriesCount = matchingSeries.Count(s => SeriesHasTag(s, t.Id)),
                        EpisodeFileCount = files.Count,
                        SizeOnDisk = files.Sum(f => f.Size)
                    };
                })
                .ToList();
        }

        // A null Quality/Quality.Quality isn't filtered out here - the real SQL's
        // CAST(JSON_EXTRACT(...) AS INTEGER) has nothing to exclude on, it just groups such rows
        // under whatever that extraction yields, and dropping them here would make this
        // breakdown's total silently undercount filesForOverallCount's EpisodeFileCount. ?? 0
        // matches DeriveQualityId's established "no quality data" convention used everywhere else
        // in this port instead.
        private static List<QualityStatistics> BuildQualityStatistics(List<EpisodeFile> files) =>
            files
                .GroupBy(f => f.Quality?.Quality?.Id ?? 0)
                .OrderBy(g => g.Key)
                .Select(g => new QualityStatistics
                {
                    Quality = Quality.FindById(g.Key),
                    EpisodeFileCount = g.Count(),
                    SizeOnDisk = g.Sum(f => f.Size)
                })
                .ToList();

        private static bool IsCompletedGroup(IEnumerable<Episode> group, Func<Episode, bool> isCounted, Func<Episode, bool> hasFile)
        {
            var episodes = group as ICollection<Episode> ?? group.ToList();
            var episodeCount = episodes.Count(isCounted);
            var fileCount = episodes.Count(hasFile);

            return episodeCount > 0 && episodeCount == fileCount;
        }

        private static bool HasAnyCondition(StatisticsFilter filter) =>
            filter != null && (
                filter.RootFolderPaths is { Count: > 0 } ||
                filter.TagIds is { Count: > 0 } ||
                filter.QualityProfileIds is { Count: > 0 } ||
                filter.Monitored.HasValue ||
                filter.SeriesTypes is { Count: > 0 });

        // Mirrors StatisticsRepository.BuildSeriesFilter/BuildConditionGroup condition for
        // condition - each active dimension becomes one predicate (OR across its own values,
        // negated as a whole if the matching *Not flag is set), all combined with AND.
        private static Func<Series, bool> BuildSeriesPredicate(StatisticsFilter filter)
        {
            if (filter == null)
            {
                return _ => true;
            }

            var predicates = new List<Func<Series, bool>>();

            if (filter.RootFolderPaths is { Count: > 0 })
            {
                var prefixes = filter.RootFolderPaths.Select(path =>
                {
                    var separator = path.Contains('\\') ? '\\' : '/';
                    return path.TrimEnd('/', '\\') + separator;
                }).ToList();

                bool MatchesAnyPrefix(Series s) => prefixes.Any(prefix => s.Path.StartsWith(prefix, StringComparison.Ordinal));

                // SQL's SUBSTR(NULL, ...) = prefix evaluates to NULL, which a WHERE clause treats
                // as "exclude" whether or not it's wrapped in NOT (three-valued logic - NOT NULL
                // is still NULL, never TRUE). So a null Path must be excluded unconditionally,
                // not as part of what gets negated - Negate() would flip it to a false match on
                // a negated filter, incorrectly including a series with no Path at all.
                predicates.Add(s => s.Path != null && (filter.RootFolderPathsNot ? !MatchesAnyPrefix(s) : MatchesAnyPrefix(s)));
            }

            if (filter.TagIds is { Count: > 0 })
            {
                predicates.Add(Negate(s => filter.TagIds.Any(id => s.Tags != null && s.Tags.Contains(id)), filter.TagIdsNot));
            }

            if (filter.QualityProfileIds is { Count: > 0 })
            {
                predicates.Add(Negate(s => filter.QualityProfileIds.Contains(s.QualityProfileId), filter.QualityProfileIdsNot));
            }

            if (filter.Monitored.HasValue)
            {
                predicates.Add(s => s.Monitored == filter.Monitored.Value);
            }

            if (filter.SeriesTypes is { Count: > 0 })
            {
                predicates.Add(Negate(s => filter.SeriesTypes.Contains(s.SeriesType), filter.SeriesTypesNot));
            }

            return s => predicates.All(p => p(s));
        }

        private static Func<Series, bool> Negate(Func<Series, bool> predicate, bool negate) =>
            negate ? (s => !predicate(s)) : predicate;
    }
}
