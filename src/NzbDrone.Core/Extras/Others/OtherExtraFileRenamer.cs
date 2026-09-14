using System.IO;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Extras.Others
{
    public interface IOtherExtraFileRenamer
    {
        Task RenameOtherExtraFile(Series series, string path);
    }

    public class OtherExtraFileRenamer : IOtherExtraFileRenamer
    {
        private readonly Logger _logger;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly ISeriesService _seriesService;
        private readonly IOtherExtraFileService _otherExtraFileService;

        public OtherExtraFileRenamer(IOtherExtraFileService otherExtraFileService,
                                     ISeriesService seriesService,
                                     IRecycleBinProvider recycleBinProvider,
                                     IDiskProvider diskProvider,
                                     Logger logger)
        {
            _logger = logger;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _seriesService = seriesService;
            _otherExtraFileService = otherExtraFileService;
        }

        public async Task RenameOtherExtraFile(Series series, string path)
        {
            if (!_diskProvider.FileExists(path))
            {
                return;
            }

            var relativePath = series.Path.GetRelativePath(path);
            var otherExtraFile = await _otherExtraFileService.FindByPath(series.Id, relativePath);

            if (otherExtraFile != null)
            {
                var newPath = path + "-orig";

                // Recycle an existing -orig file.
                await RemoveOtherExtraFile(series, newPath);

                // Rename the file to .*-orig
                _diskProvider.MoveFile(path, newPath);
                otherExtraFile.RelativePath = relativePath + "-orig";
                otherExtraFile.Extension += "-orig";
                await _otherExtraFileService.Upsert(otherExtraFile);
            }
        }

        private async Task RemoveOtherExtraFile(Series series, string path)
        {
            if (!_diskProvider.FileExists(path))
            {
                return;
            }

            var relativePath = series.Path.GetRelativePath(path);
            var otherExtraFile = await _otherExtraFileService.FindByPath(series.Id, relativePath);

            if (otherExtraFile != null)
            {
                var subfolder = Path.GetDirectoryName(relativePath);
                _recycleBinProvider.DeleteFile(path, subfolder);
            }
        }
    }
}
