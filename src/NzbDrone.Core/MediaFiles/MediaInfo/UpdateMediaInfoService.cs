using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.MediaInfo
{
    public interface IUpdateMediaInfo
    {
        Task<bool> Update(EpisodeFile episodeFile, Series series);
        Task<bool> UpdateMediaInfo(EpisodeFile episodeFile, Series series);
    }

    public class UpdateMediaInfoService : IUpdateMediaInfo, IHandle<SeriesScannedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IVideoFileInfoReader _videoFileInfoReader;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public UpdateMediaInfoService(IDiskProvider diskProvider,
                                IMediaFileService mediaFileService,
                                IVideoFileInfoReader videoFileInfoReader,
                                IConfigService configService,
                                Logger logger)
        {
            _diskProvider = diskProvider;
            _mediaFileService = mediaFileService;
            _videoFileInfoReader = videoFileInfoReader;
            _configService = configService;
            _logger = logger;
        }

        // IHandle<T> is a shared, synchronous, app-wide eventing interface - its signature can't change here.
        // Bridging with GetAwaiter().GetResult() is safe: EventAggregator runs handlers off the request thread
        // via Task.Factory.StartNew, and ASP.NET Core carries no SynchronizationContext.
        public void Handle(SeriesScannedEvent message)
        {
            if (!_configService.EnableMediaInfo)
            {
                _logger.Debug("MediaInfo is disabled");
                return;
            }

            var allMediaFiles = _mediaFileService.GetFilesBySeries(message.Series.Id).GetAwaiter().GetResult();
            var filteredMediaFiles = allMediaFiles.Where(c =>
                c.MediaInfo == null ||
                c.MediaInfo.SchemaRevision < VideoFileInfoReader.MINIMUM_MEDIA_INFO_SCHEMA_REVISION).ToList();

            foreach (var mediaFile in filteredMediaFiles)
            {
                UpdateMediaInfo(mediaFile, message.Series).GetAwaiter().GetResult();
            }
        }

        public async Task<bool> Update(EpisodeFile episodeFile, Series series)
        {
            if (!_configService.EnableMediaInfo)
            {
                _logger.Debug("MediaInfo is disabled");
                return false;
            }

            return await UpdateMediaInfo(episodeFile, series);
        }

        public async Task<bool> UpdateMediaInfo(EpisodeFile episodeFile, Series series)
        {
            var path = episodeFile.Path.IsNotNullOrWhiteSpace() ? episodeFile.Path : Path.Combine(series.Path, episodeFile.RelativePath);

            if (!_diskProvider.FileExists(path))
            {
                _logger.Debug("Can't update MediaInfo because '{0}' does not exist", path);
                return false;
            }

            var updatedMediaInfo = _videoFileInfoReader.GetMediaInfo(path);

            if (updatedMediaInfo == null)
            {
                return false;
            }

            episodeFile.MediaInfo = updatedMediaInfo;

            if (episodeFile.Id != 0)
            {
                await _mediaFileService.Update(episodeFile);
            }

            _logger.Debug("Updated MediaInfo for '{0}'", path);

            return true;
        }
    }
}
