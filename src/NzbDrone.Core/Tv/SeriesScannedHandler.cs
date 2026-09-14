using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public class SeriesScannedHandler : IHandle<SeriesScannedEvent>,
                                        IHandle<SeriesScanSkippedEvent>
    {
        private readonly IEpisodeMonitoredService _episodeMonitoredService;
        private readonly ISeriesService _seriesService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IEpisodeRefreshedService _episodeRefreshedService;
        private readonly IEventAggregator _eventAggregator;

        private readonly Logger _logger;

        public SeriesScannedHandler(IEpisodeMonitoredService episodeMonitoredService,
                                    ISeriesService seriesService,
                                    IManageCommandQueue commandQueueManager,
                                    IEpisodeRefreshedService episodeRefreshedService,
                                    IEventAggregator eventAggregator,
                                    Logger logger)
        {
            _episodeMonitoredService = episodeMonitoredService;
            _seriesService = seriesService;
            _commandQueueManager = commandQueueManager;
            _episodeRefreshedService = episodeRefreshedService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private async Task HandleScanEvents(Series series)
        {
            var addOptions = series.AddOptions;

            if (addOptions == null)
            {
                await _episodeRefreshedService.Search(series);
                return;
            }

            _logger.Info("[{0}] was recently added, performing post-add actions", series.Title);
            await _episodeMonitoredService.SetEpisodeMonitoredStatus(series, addOptions);

            _eventAggregator.PublishEvent(new SeriesAddCompletedEvent(series));

            // If both options are enabled search for the whole series, which will only include monitored episodes.
            // This way multiple searches for the same season are skipped, though a season that can't be upgraded may be
            // searched, but the logs will be more explicit.

            if (addOptions.SearchForMissingEpisodes && addOptions.SearchForCutoffUnmetEpisodes)
            {
                await _commandQueueManager.Push(new SeriesSearchCommand(series.Id));
            }
            else
            {
                if (addOptions.SearchForMissingEpisodes)
                {
                    await _commandQueueManager.Push(new MissingEpisodeSearchCommand(series.Id));
                }

                if (addOptions.SearchForCutoffUnmetEpisodes)
                {
                    await _commandQueueManager.Push(new CutoffUnmetEpisodeSearchCommand(series.Id));
                }
            }

            series.AddOptions = null;
            await _seriesService.RemoveAddOptions(series);
        }

        // IHandle<T> is a shared, synchronous, app-wide eventing interface (50+ implementers) - its signature
        // can't change here. EventAggregator invokes Handle() off the request thread via Task.Factory.StartNew,
        // and ASP.NET Core carries no SynchronizationContext, so bridging with GetAwaiter().GetResult() is safe
        // (same reasoning already applied elsewhere this session).
        public void Handle(SeriesScannedEvent message)
        {
            HandleScanEvents(message.Series).GetAwaiter().GetResult();
        }

        public void Handle(SeriesScanSkippedEvent message)
        {
            HandleScanEvents(message.Series).GetAwaiter().GetResult();
        }
    }
}
