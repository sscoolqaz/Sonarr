using System.IO;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv.Commands;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Tv
{
    public class MoveSeriesService : IExecute<MoveSeriesCommand>, IExecute<BulkMoveSeriesCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly IBuildFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MoveSeriesService(ISeriesService seriesService,
                                 IBuildFileNames filenameBuilder,
                                 IDiskProvider diskProvider,
                                 IDiskTransferService diskTransferService,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _seriesService = seriesService;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        private async Task MoveSingleSeries(Series series, string sourcePath, string destinationPath, int? index = null, int? total = null)
        {
            if (!sourcePath.IsPathValid(PathValidationType.CurrentOs))
            {
                _logger.Warn("Folder '{0}' for '{1}' is invalid, unable to move series. Try moving files manually", sourcePath, series.Title);
                return;
            }

            if (!_diskProvider.FolderExists(sourcePath))
            {
                _logger.Debug("Folder '{0}' for '{1}' does not exist, not moving.", sourcePath, series.Title);
                return;
            }

            if (index != null && total != null)
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}' ({3}/{4})", series.Title, sourcePath, destinationPath, index + 1, total);
            }
            else
            {
                _logger.ProgressInfo("Moving {0} from '{1}' to '{2}'", series.Title, sourcePath, destinationPath);
            }

            if (sourcePath.PathEquals(destinationPath))
            {
                _logger.ProgressInfo("{0} is already in the specified location '{1}'.", series, destinationPath);
                return;
            }

            try
            {
                // Ensure the parent of the series folder exists, this will often just be the root folder, but
                // in cases where people are using subfolders for first letter (etc) it may not yet exist.
                _diskProvider.CreateFolder(new DirectoryInfo(destinationPath).Parent.FullName);
                _diskTransferService.TransferFolder(sourcePath, destinationPath, TransferMode.Move);

                _logger.ProgressInfo("{0} moved successfully to {1}", series.Title, destinationPath);

                _eventAggregator.PublishEvent(new SeriesMovedEvent(series, sourcePath, destinationPath));
            }
            catch (IOException ex)
            {
                _logger.Error(ex, "Unable to move series from '{0}' to '{1}'. Try moving files manually", sourcePath, destinationPath);

                await RevertPath(series.Id, sourcePath);
            }
        }

        private async Task RevertPath(int seriesId, string path)
        {
            var series = await _seriesService.GetSeries(seriesId);

            series.Path = path;
            await _seriesService.UpdateSeries(series);
        }

        // IExecute<TCommand> is a shared, synchronous, app-wide command interface - its signature can't change
        // here. Bridging with GetAwaiter().GetResult() is safe for the same reason as the IHandle bridges
        // elsewhere this session (no SynchronizationContext on the threads command execution runs on).
        public void Execute(MoveSeriesCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId).GetAwaiter().GetResult();
            MoveSingleSeries(series, message.SourcePath, message.DestinationPath).GetAwaiter().GetResult();
        }

        public void Execute(BulkMoveSeriesCommand message)
        {
            var seriesToMove = message.Series;
            var destinationRootFolder = message.DestinationRootFolder;

            _logger.ProgressInfo("Moving {0} series to '{1}'", seriesToMove.Count, destinationRootFolder);

            for (var index = 0; index < seriesToMove.Count; index++)
            {
                var s = seriesToMove[index];
                var series = _seriesService.GetSeries(s.SeriesId).GetAwaiter().GetResult();
                var destinationPath = Path.Combine(destinationRootFolder, _filenameBuilder.GetSeriesFolder(series).GetAwaiter().GetResult());

                MoveSingleSeries(series, s.SourcePath, destinationPath, index, seriesToMove.Count).GetAwaiter().GetResult();
            }

            _logger.ProgressInfo("Finished moving {0} series to '{1}'", seriesToMove.Count, destinationRootFolder);
        }
    }
}
