using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public interface IEpisodeRefreshedService
    {
        Task Search(Series series);
    }

    public class EpisodeRefreshedService : IEpisodeRefreshedService, IHandle<EpisodeInfoRefreshedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;
        private readonly ICached<List<int>> _searchCache;

        public EpisodeRefreshedService(ICacheManager cacheManager,
                                   IManageCommandQueue commandQueueManager,
                                   IEpisodeService episodeService,
                                   Logger logger)
        {
            _commandQueueManager = commandQueueManager;
            _episodeService = episodeService;
            _logger = logger;
            _searchCache = cacheManager.GetCache<List<int>>(GetType());
        }

        public async Task Search(Series series)
        {
            var previouslyAired = _searchCache.Find(series.Id.ToString());

            if (previouslyAired != null && previouslyAired.Any())
            {
                var missing = new List<Episode>();

                foreach (var episodeId in previouslyAired)
                {
                    var episode = await _episodeService.GetEpisode(episodeId);

                    if (!episode.HasFile)
                    {
                        missing.Add(episode);
                    }
                }

                if (missing.Any())
                {
                    _logger.Info("Searching for {MissingCount} episodes from '{SeriesTitle}' that were recently added or had absolute episode number added", missing.Count, series.Title);
                    await _commandQueueManager.Push(new EpisodeSearchCommand(missing.Select(e => e.Id).ToList()));
                }
            }

            _searchCache.Remove(series.Id.ToString());
        }

        public void Handle(EpisodeInfoRefreshedEvent message)
        {
            if (message.Series.AddOptions == null)
            {
                var toSearch = new List<int>();

                if (!message.Series.Monitored)
                {
                    _logger.Debug("Series is not monitored");
                    return;
                }

                var previouslyAired = message.Added.Where(a =>
                        a.AirDateUtc.HasValue &&
                        a.AirDateUtc.Value.Between(DateTime.UtcNow.AddDays(-14), DateTime.UtcNow.AddDays(1)) &&
                        a.Monitored)
                    .Select(e => e.Id)
                    .ToList();

                if (previouslyAired.Empty())
                {
                    _logger.Debug("Newly added episodes all air in the future");
                }

                toSearch.AddRange(previouslyAired);

                var absoluteEpisodeNumberAdded = message.Updated.Where(a =>
                        a.AbsoluteEpisodeNumberAdded &&
                        a.AirDateUtc.HasValue &&
                        a.AirDateUtc.Value.Between(DateTime.UtcNow.AddDays(-14), DateTime.UtcNow.AddDays(1)) &&
                        a.Monitored)
                    .Select(e => e.Id)
                    .ToList();

                if (absoluteEpisodeNumberAdded.Empty())
                {
                    _logger.Debug("No updated episodes recently aired and had absolute episode number added");
                }

                toSearch.AddRange(absoluteEpisodeNumberAdded);

                if (toSearch.Any())
                {
                    _searchCache.Set(message.Series.Id.ToString(), toSearch.Distinct().ToList());
                }
            }
        }
    }
}
