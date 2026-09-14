using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface ISeriesService
    {
        Task<Series> GetSeries(int seriesId);
        Task<List<Series>> GetSeries(IEnumerable<int> seriesIds);
        Task<Series> AddSeries(Series newSeries);
        Task<List<Series>> AddSeries(List<Series> newSeries);
        Task<Series> FindByTvdbId(int tvdbId);
        Task<Series> FindByTvRageId(int tvRageId);
        Task<Series> FindByImdbId(string imdbId);
        Task<Series> FindByTitle(string title);
        Task<Series> FindByTitle(string title, int year);
        Task<Series> FindByTitleInexact(string title);
        Task<Series> FindByPath(string path);
        Task DeleteSeries(List<int> seriesIds, bool deleteFiles, bool addImportListExclusion);
        Task<List<Series>> GetAllSeries();
        Task<Dictionary<int, int>> AllSeriesTvdbIds();
        Task<Dictionary<int, string>> GetAllSeriesPaths();
        Task<Dictionary<int, List<int>>> GetAllSeriesTags();
        Task<List<Series>> AllForTag(int tagId);
        Task<Dictionary<int, int>> GetAllSeriesQualityProfiles();
        Task<Series> UpdateSeries(Series series, bool updateEpisodesToMatchSeason = true, bool publishUpdatedEvent = true);
        Task<List<Series>> UpdateSeries(List<Series> series, bool useExistingRelativeFolder);
        Task<bool> SeriesPathExists(string folder);
        Task RemoveAddOptions(Series series);
        Task<bool> UpdateAutoTaggingTags(Series series);
        Task UpdateTags(List<Series> series);
    }

    public class SeriesService : ISeriesService
    {
        private readonly ISeriesRepository _seriesRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IEpisodeService _episodeService;
        private readonly IBuildSeriesPaths _seriesPathBuilder;
        private readonly IAutoTaggingService _autoTaggingService;
        private readonly Logger _logger;

        public SeriesService(ISeriesRepository seriesRepository,
                             IEventAggregator eventAggregator,
                             IEpisodeService episodeService,
                             IBuildSeriesPaths seriesPathBuilder,
                             IAutoTaggingService autoTaggingService,
                             Logger logger)
        {
            _seriesRepository = seriesRepository;
            _eventAggregator = eventAggregator;
            _episodeService = episodeService;
            _seriesPathBuilder = seriesPathBuilder;
            _autoTaggingService = autoTaggingService;
            _logger = logger;
        }

        public async Task<Series> GetSeries(int seriesId)
        {
            return await _seriesRepository.Get(seriesId);
        }

        public async Task<List<Series>> GetSeries(IEnumerable<int> seriesIds)
        {
            return (await _seriesRepository.Get(seriesIds)).ToList();
        }

        public async Task<Series> AddSeries(Series newSeries)
        {
            await _seriesRepository.Insert(newSeries);
            _eventAggregator.PublishEvent(new SeriesAddedEvent(await GetSeries(newSeries.Id)));

            return newSeries;
        }

        public async Task<List<Series>> AddSeries(List<Series> newSeries)
        {
            await _seriesRepository.InsertMany(newSeries);
            _eventAggregator.PublishEvent(new SeriesImportedEvent(newSeries.Select(s => s.Id).ToList()));

            return newSeries;
        }

        public async Task<Series> FindByTvdbId(int tvRageId)
        {
            return await _seriesRepository.FindByTvdbId(tvRageId);
        }

        public async Task<Series> FindByTvRageId(int tvRageId)
        {
            return await _seriesRepository.FindByTvRageId(tvRageId);
        }

        public async Task<Series> FindByImdbId(string imdbId)
        {
            return await _seriesRepository.FindByImdbId(imdbId);
        }

        public async Task<Series> FindByTitle(string title)
        {
            return await _seriesRepository.FindByTitle(title.CleanSeriesTitle());
        }

        public async Task<Series> FindByTitleInexact(string title)
        {
            // find any series clean title within the provided release title
            var cleanTitle = title.CleanSeriesTitle();
            var list = await _seriesRepository.FindByTitleInexact(cleanTitle);
            if (!list.Any())
            {
                // no series matched
                return null;
            }

            if (list.Count == 1)
            {
                // return the first series if there is only one
                return list.Single();
            }

            // build ordered list of series by position in the search string
            var query =
                list.Select(series => new
                {
                    position = cleanTitle.IndexOf(series.CleanTitle),
                    length = series.CleanTitle.Length,
                    series = series
                })
                    .Where(s => (s.position >= 0))
                    .ToList()
                    .OrderBy(s => s.position)
                    .ThenByDescending(s => s.length)
                    .ToList();

            // get the leftmost series that is the longest
            // series are usually the first thing in release title, so we select the leftmost and longest match
            var match = query.First().series;

            _logger.Debug("Multiple series matched {0} from title {1}", match.Title, title);
            foreach (var entry in list)
            {
                _logger.Debug("Multiple series match candidate: {0} cleantitle: {1}", entry.Title, entry.CleanTitle);
            }

            return match;
        }

        public async Task<Series> FindByPath(string path)
        {
            return await _seriesRepository.FindByPath(path);
        }

        public async Task<Series> FindByTitle(string title, int year)
        {
            return await _seriesRepository.FindByTitle(title.CleanSeriesTitle(), year);
        }

        public async Task DeleteSeries(List<int> seriesIds, bool deleteFiles, bool addImportListExclusion)
        {
            var series = (await _seriesRepository.Get(seriesIds)).ToList();
            await _seriesRepository.DeleteMany(seriesIds);
            _eventAggregator.PublishEvent(new SeriesDeletedEvent(series, deleteFiles, addImportListExclusion));
        }

        public async Task<List<Series>> GetAllSeries()
        {
            return (await _seriesRepository.All()).ToList();
        }

        public async Task<Dictionary<int, int>> AllSeriesTvdbIds()
        {
            return await _seriesRepository.AllSeriesTvdbIds();
        }

        public async Task<Dictionary<int, string>> GetAllSeriesPaths()
        {
            return await _seriesRepository.AllSeriesPaths();
        }

        public async Task<Dictionary<int, List<int>>> GetAllSeriesTags()
        {
            return await _seriesRepository.AllSeriesTags();
        }

        public async Task<Dictionary<int, int>> GetAllSeriesQualityProfiles()
        {
            return await _seriesRepository.AllSeriesQualityProfiles();
        }

        public async Task<List<Series>> AllForTag(int tagId)
        {
            return (await GetAllSeries()).Where(s => s.Tags.Contains(tagId))
                                 .ToList();
        }

        // updateEpisodesToMatchSeason is an override for EpisodeMonitoredService to use so a change via Season pass doesn't get nuked by the seasons loop.
        // TODO: Remove when seasons are split from series (or we come up with a better way to address this)
        public async Task<Series> UpdateSeries(Series series, bool updateEpisodesToMatchSeason = true, bool publishUpdatedEvent = true)
        {
            var storedSeries = await GetSeries(series.Id);

            var episodeMonitoredChanged = false;

            if (updateEpisodesToMatchSeason)
            {
                foreach (var season in series.Seasons)
                {
                    var storedSeason = storedSeries.Seasons.SingleOrDefault(s => s.SeasonNumber == season.SeasonNumber);

                    if (storedSeason != null && season.Monitored != storedSeason.Monitored)
                    {
                        await _episodeService.SetEpisodeMonitoredBySeason(series.Id, season.SeasonNumber, season.Monitored);
                        episodeMonitoredChanged = true;
                    }
                }
            }

            // Never update AddOptions when updating a series, keep it the same as the existing stored series.
            series.AddOptions = storedSeries.AddOptions;
            await UpdateAutoTaggingTags(series);

            var updatedSeries = await _seriesRepository.Update(series);
            if (publishUpdatedEvent)
            {
                _eventAggregator.PublishEvent(new SeriesEditedEvent(updatedSeries, storedSeries, episodeMonitoredChanged));
            }

            return updatedSeries;
        }

        public async Task<List<Series>> UpdateSeries(List<Series> series, bool useExistingRelativeFolder)
        {
            _logger.Debug("Updating {0} series", series.Count);

            foreach (var s in series)
            {
                _logger.Trace("Updating: {0}", s.Title);

                if (!s.RootFolderPath.IsNullOrWhiteSpace())
                {
                    s.Path = await _seriesPathBuilder.BuildPath(s, useExistingRelativeFolder);

                    _logger.Trace("Changing path for {0} to {1}", s.Title, s.Path);
                }
                else
                {
                    _logger.Trace("Not changing path for: {0}", s.Title);
                }

                await UpdateAutoTaggingTags(s);
            }

            await _seriesRepository.UpdateMany(series);
            _logger.Debug("{0} series updated", series.Count);
            _eventAggregator.PublishEvent(new SeriesBulkEditedEvent(series));

            return series;
        }

        public async Task<bool> SeriesPathExists(string folder)
        {
            return await _seriesRepository.SeriesPathExists(folder);
        }

        public async Task RemoveAddOptions(Series series)
        {
            await _seriesRepository.SetFields(series, s => s.AddOptions);
        }

        public async Task<bool> UpdateAutoTaggingTags(Series series)
        {
            _logger.Trace("Updating tags for {0}", series);

            var tagsAdded = new HashSet<int>();
            var tagsRemoved = new HashSet<int>();
            var changes = await _autoTaggingService.GetTagChanges(series);

            foreach (var tag in changes.TagsToRemove)
            {
                if (series.Tags.Contains(tag))
                {
                    series.Tags.Remove(tag);
                    tagsRemoved.Add(tag);
                }
            }

            foreach (var tag in changes.TagsToAdd)
            {
                if (!series.Tags.Contains(tag))
                {
                    series.Tags.Add(tag);
                    tagsAdded.Add(tag);
                }
            }

            if (tagsAdded.Any() || tagsRemoved.Any())
            {
                _logger.Debug("Updated tags for '{0}'. Added: {1}, Removed: {2}", series.Title, tagsAdded.Count, tagsRemoved.Count);

                return true;
            }

            _logger.Debug("Tags not updated for '{0}'", series.Title);

            return false;
        }

        public async Task UpdateTags(List<Series> series)
        {
            if (series.Count == 0)
            {
                return;
            }

            await _seriesRepository.SetFields(series, s => s.Tags);
            _eventAggregator.PublishEvent(new SeriesBulkEditedEvent(series));
        }
    }
}
