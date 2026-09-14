using System.IO;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Extras.Metadata.Files
{
    public interface ICleanMetadataService
    {
        Task Clean(Series series);
    }

    public class CleanExtraFileService : ICleanMetadataService
    {
        private readonly IMetadataFileService _metadataFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public CleanExtraFileService(IMetadataFileService metadataFileService,
                                    IDiskProvider diskProvider,
                                    Logger logger)
        {
            _metadataFileService = metadataFileService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public async Task Clean(Series series)
        {
            _logger.Debug("Cleaning missing metadata files for series: {0}", series.Title);

            var metadataFiles = await _metadataFileService.GetFilesBySeries(series.Id);

            foreach (var metadataFile in metadataFiles)
            {
                if (!_diskProvider.FileExists(Path.Combine(series.Path, metadataFile.RelativePath)))
                {
                    _logger.Debug("Deleting metadata file from database: {0}", metadataFile.RelativePath);
                    await _metadataFileService.Delete(metadataFile.Id);
                }
            }
        }
    }
}
