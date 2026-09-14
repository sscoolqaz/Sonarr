using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface IEpisodeService
    {
        Task<Episode> GetEpisode(int id);
        Task<List<Episode>> GetEpisodes(IEnumerable<int> ids);
        Task<Episode> FindEpisode(int seriesId, int seasonNumber, int episodeNumber);
        Task<Episode> FindEpisode(int seriesId, int absoluteEpisodeNumber);
        Task<Episode> FindEpisodeByTitle(int seriesId, int seasonNumber, string releaseTitle);
        Task<List<Episode>> FindEpisodesBySceneNumbering(int seriesId, int seasonNumber, int episodeNumber);
        Task<List<Episode>> FindEpisodesBySceneNumbering(int seriesId, int sceneAbsoluteEpisodeNumber);
        Task<Episode> FindEpisode(int seriesId, string date, int? part);
        Task<List<Episode>> GetEpisodeBySeries(int seriesId);
        Task<List<Episode>> GetEpisodesBySeries(List<int> seriesIds);
        Task<List<Episode>> GetEpisodesBySeason(int seriesId, int seasonNumber);
        Task<List<Episode>> GetEpisodesBySceneSeason(int seriesId, int sceneSeasonNumber);
        Task<List<Episode>> EpisodesWithFiles(int seriesId);
        Task<PagingSpec<Episode>> EpisodesWithoutFiles(PagingSpec<Episode> pagingSpec, bool includeSpecials, HashSet<int> seriesTags = null);
        Task<List<Episode>> GetEpisodesByFileId(int episodeFileId);
        Task UpdateEpisode(Episode episode);
        Task SetEpisodeMonitored(int episodeId, bool monitored);
        Task SetMonitored(IEnumerable<int> ids, bool monitored);
        Task UpdateEpisodes(List<Episode> episodes);
        Task UpdateLastSearchTime(List<Episode> episodes);
        Task<List<Episode>> EpisodesBetweenDates(DateTime start, DateTime end, bool includeUnmonitored, bool includeSpecials);
        Task InsertMany(List<Episode> episodes);
        Task UpdateMany(List<Episode> episodes);
        Task DeleteMany(List<Episode> episodes);
        Task SetEpisodeMonitoredBySeason(int seriesId, int seasonNumber, bool monitored);
        Task<List<int>> SetEpisodeMonitoredBySeries(int seriesId, MonitorTypes monitor, int firstSeason, int lastSeason);
    }

    public class EpisodeService : IEpisodeService,
                                  IHandle<EpisodeFileDeletedEvent>,
                                  IHandle<EpisodeFileAddedEvent>,
                                  IHandleAsync<SeriesDeletedEvent>,
                                  IHandleAsync<SeriesScannedEvent>
    {
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IConfigService _configService;
        private readonly ICached<HashSet<int>> _cache;
        private readonly Logger _logger;

        public EpisodeService(IEpisodeRepository episodeRepository, IConfigService configService, ICacheManager cacheManager, Logger logger)
        {
            _episodeRepository = episodeRepository;
            _configService = configService;
            _cache = cacheManager.GetCache<HashSet<int>>(GetType());
            _logger = logger;
        }

        public async Task<Episode> GetEpisode(int id)
        {
            return await _episodeRepository.Get(id);
        }

        public async Task<List<Episode>> GetEpisodes(IEnumerable<int> ids)
        {
            return (await _episodeRepository.Get(ids)).ToList();
        }

        public async Task<Episode> FindEpisode(int seriesId, int seasonNumber, int episodeNumber)
        {
            return await _episodeRepository.Find(seriesId, seasonNumber, episodeNumber);
        }

        public async Task<Episode> FindEpisode(int seriesId, int absoluteEpisodeNumber)
        {
            return await _episodeRepository.Find(seriesId, absoluteEpisodeNumber);
        }

        public async Task<List<Episode>> FindEpisodesBySceneNumbering(int seriesId, int seasonNumber, int episodeNumber)
        {
            return await _episodeRepository.FindEpisodesBySceneNumbering(seriesId, seasonNumber, episodeNumber);
        }

        public async Task<List<Episode>> FindEpisodesBySceneNumbering(int seriesId, int sceneAbsoluteEpisodeNumber)
        {
            return await _episodeRepository.FindEpisodesBySceneNumbering(seriesId, sceneAbsoluteEpisodeNumber);
        }

        public async Task<Episode> FindEpisode(int seriesId, string date, int? part)
        {
            return await FindOneByAirDate(seriesId, date, part);
        }

        public async Task<List<Episode>> GetEpisodeBySeries(int seriesId)
        {
            return (await _episodeRepository.GetEpisodes(seriesId)).ToList();
        }

        public async Task<List<Episode>> GetEpisodesBySeries(List<int> seriesIds)
        {
            return (await _episodeRepository.GetEpisodesBySeriesIds(seriesIds)).ToList();
        }

        public async Task<List<Episode>> GetEpisodesBySeason(int seriesId, int seasonNumber)
        {
            return await _episodeRepository.GetEpisodes(seriesId, seasonNumber);
        }

        public async Task<List<Episode>> GetEpisodesBySceneSeason(int seriesId, int sceneSeasonNumber)
        {
            return await _episodeRepository.GetEpisodesBySceneSeason(seriesId, sceneSeasonNumber);
        }

        public async Task<Episode> FindEpisodeByTitle(int seriesId, int seasonNumber, string releaseTitle)
        {
            // TODO: can replace this search mechanism with something smarter/faster/better
            var normalizedReleaseTitle = Parser.Parser.NormalizeEpisodeTitle(releaseTitle);
            var cleanNormalizedReleaseTitle = Parser.Parser.CleanSeriesTitle(normalizedReleaseTitle);
            var episodes = await _episodeRepository.GetEpisodes(seriesId, seasonNumber);

            var possibleMatches = episodes.SelectMany(
                episode => new[]
                {
                    new
                    {
                        Position = normalizedReleaseTitle.IndexOf(Parser.Parser.NormalizeEpisodeTitle(episode.Title), StringComparison.CurrentCultureIgnoreCase),
                        Length = Parser.Parser.NormalizeEpisodeTitle(episode.Title).Length,
                        Episode = episode
                    },
                    new
                    {
                        Position = cleanNormalizedReleaseTitle.IndexOf(Parser.Parser.CleanSeriesTitle(Parser.Parser.NormalizeEpisodeTitle(episode.Title)), StringComparison.CurrentCultureIgnoreCase),
                        Length = Parser.Parser.NormalizeEpisodeTitle(episode.Title).Length,
                        Episode = episode
                    }
                });

            var matches = possibleMatches
                                .Where(e => e.Episode.Title.Length > 0 && e.Position >= 0)
                                .OrderBy(e => e.Position)
                                .ThenByDescending(e => e.Length)
                                .ToList();

            if (matches.Any())
            {
                return matches.First().Episode;
            }

            return null;
        }

        public async Task<List<Episode>> EpisodesWithFiles(int seriesId)
        {
            return await _episodeRepository.EpisodesWithFiles(seriesId);
        }

        public async Task<PagingSpec<Episode>> EpisodesWithoutFiles(PagingSpec<Episode> pagingSpec, bool includeSpecials, HashSet<int> seriesTags = null)
        {
            return await _episodeRepository.EpisodesWithoutFiles(pagingSpec, includeSpecials, seriesTags);
        }

        public async Task<List<Episode>> GetEpisodesByFileId(int episodeFileId)
        {
            return await _episodeRepository.GetEpisodeByFileId(episodeFileId);
        }

        public async Task UpdateEpisode(Episode episode)
        {
            await _episodeRepository.Update(episode);
        }

        public async Task SetEpisodeMonitored(int episodeId, bool monitored)
        {
            var episode = await _episodeRepository.Get(episodeId);
            await _episodeRepository.SetMonitoredFlat(episode, monitored);

            _logger.Debug("Monitored flag for Episode:{0} was set to {1}", episodeId, monitored);
        }

        public async Task SetMonitored(IEnumerable<int> ids, bool monitored)
        {
            await _episodeRepository.SetMonitored(ids, monitored);
        }

        public async Task SetEpisodeMonitoredBySeason(int seriesId, int seasonNumber, bool monitored)
        {
            await _episodeRepository.SetMonitoredBySeason(seriesId, seasonNumber, monitored);
        }

        public async Task<List<int>> SetEpisodeMonitoredBySeries(int seriesId, MonitorTypes monitor, int firstSeason, int lastSeason)
        {
            return await _episodeRepository.SetMonitored(seriesId, monitor, firstSeason, lastSeason);
        }

        public async Task UpdateEpisodes(List<Episode> episodes)
        {
            await _episodeRepository.UpdateMany(episodes);
        }

        public async Task UpdateLastSearchTime(List<Episode> episodes)
        {
            await _episodeRepository.SetFields(episodes, e => e.LastSearchTime);
        }

        public async Task<List<Episode>> EpisodesBetweenDates(DateTime start, DateTime end, bool includeUnmonitored, bool includeSpecials)
        {
            var episodes = await _episodeRepository.EpisodesBetweenDates(start.ToUniversalTime(), end.ToUniversalTime(), includeUnmonitored, includeSpecials);

            return episodes;
        }

        public async Task InsertMany(List<Episode> episodes)
        {
            await _episodeRepository.InsertMany(episodes);
        }

        public async Task UpdateMany(List<Episode> episodes)
        {
            await _episodeRepository.UpdateMany(episodes);
        }

        public async Task DeleteMany(List<Episode> episodes)
        {
            await _episodeRepository.DeleteMany(episodes);
        }

        private async Task<Episode> FindOneByAirDate(int seriesId, string date, int? part)
        {
            var episodes = await _episodeRepository.Find(seriesId, date);

            if (!episodes.Any())
            {
                return null;
            }

            if (episodes.Count == 1)
            {
                return episodes.First();
            }

            _logger.Debug("Multiple episodes with the same air date were found, will exclude specials");

            var regularEpisodes = episodes.Where(e => e.SeasonNumber > 0).ToList();

            if (regularEpisodes.Count == 1 && !part.HasValue)
            {
                _logger.Debug("Left with one episode after excluding specials");
                return regularEpisodes.First();
            }
            else if (part.HasValue && part.Value <= regularEpisodes.Count)
            {
                var sortedEpisodes = regularEpisodes.OrderBy(e => e.SeasonNumber)
                                                               .ThenBy(e => e.EpisodeNumber)
                                                                .ToList();

                return sortedEpisodes[part.Value - 1];
            }

            throw new InvalidOperationException($"Multiple episodes with the same air date found. Date: {date}");
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide); its
        // `void Handle(TEvent message)` signature is out of scope to convert (see architectural
        // note in ProviderFactory.cs). EventAggregator dispatches sync handlers inline, but not on
        // an ASP.NET Core request thread (no captured SynchronizationContext), so blocking via
        // GetAwaiter().GetResult() is the documented boundary here rather than a silent scatter.
        public void Handle(EpisodeFileDeletedEvent message)
        {
            foreach (var episode in GetEpisodesByFileId(message.EpisodeFile.Id).GetAwaiter().GetResult())
            {
                _logger.Debug("Detaching episode {0} from file.", episode.Id);

                var unmonitorEpisodes = _configService.AutoUnmonitorPreviouslyDownloadedEpisodes;

                var unmonitorForReason = message.Reason != DeleteMediaFileReason.Upgrade &&
                                         message.Reason != DeleteMediaFileReason.ManualOverride &&
                                         message.Reason != DeleteMediaFileReason.MissingFromDisk;

                // If episode is being unlinked because it's missing from disk store it for
                if (message.Reason == DeleteMediaFileReason.MissingFromDisk && unmonitorEpisodes)
                {
                    lock (_cache)
                    {
                        var ids = _cache.Get(episode.SeriesId.ToString(), () => new HashSet<int>());

                        ids.Add(episode.Id);
                    }
                }

                _episodeRepository.ClearFileId(episode, unmonitorForReason && unmonitorEpisodes).GetAwaiter().GetResult();
            }
        }

        public void Handle(EpisodeFileAddedEvent message)
        {
            foreach (var episode in message.EpisodeFile.Episodes.Value)
            {
                _episodeRepository.SetFileId(episode, message.EpisodeFile.Id).GetAwaiter().GetResult();

                lock (_cache)
                {
                    var ids = _cache.Find(episode.SeriesId.ToString());

                    if (ids?.Contains(episode.Id) == true)
                    {
                        ids.Remove(episode.Id);
                    }
                }

                _logger.Debug("Linking [{0}] > [{1}]", message.EpisodeFile.RelativePath, episode);
            }
        }

        // NOTE: IHandleAsync<TEvent> handlers are dispatched by EventAggregator via
        // Task.Factory.StartNew on a thread-pool thread (see EventAggregator.cs), not a request
        // thread, so blocking here is safe from a sync-context deadlock. Converting the shared
        // `void HandleAsync(TEvent message)` interface itself is out of scope (18 implementers).
        public void HandleAsync(SeriesDeletedEvent message)
        {
            var episodes = _episodeRepository.GetEpisodesBySeriesIds(message.Series.Select(s => s.Id).ToList()).GetAwaiter().GetResult();
            _episodeRepository.DeleteMany(episodes).GetAwaiter().GetResult();
        }

        public void HandleAsync(SeriesScannedEvent message)
        {
            lock (_cache)
            {
                var ids = _cache.Find(message.Series.Id.ToString());

                if (ids?.Any() == true)
                {
                    _episodeRepository.SetMonitored(ids, false).GetAwaiter().GetResult();
                }

                _cache.Remove(message.Series.Id.ToString());
            }
        }
    }
}
