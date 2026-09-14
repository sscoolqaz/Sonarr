using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Aggregation
{
    public interface IRemoteEpisodeAggregationService
    {
        Task<RemoteEpisode> Augment(RemoteEpisode remoteEpisode);
    }

    public class RemoteEpisodeAggregationService : IRemoteEpisodeAggregationService
    {
        private readonly IEnumerable<IAggregateRemoteEpisode> _augmenters;
        private readonly Logger _logger;

        public RemoteEpisodeAggregationService(IEnumerable<IAggregateRemoteEpisode> augmenters,
                                  Logger logger)
        {
            _augmenters = augmenters;
            _logger = logger;
        }

        public async Task<RemoteEpisode> Augment(RemoteEpisode remoteEpisode)
        {
            if (remoteEpisode == null)
            {
                return null;
            }

            foreach (var augmenter in _augmenters)
            {
                try
                {
                    await augmenter.Aggregate(remoteEpisode);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, ex.Message);
                }
            }

            return remoteEpisode;
        }
    }
}
