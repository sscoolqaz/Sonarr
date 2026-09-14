using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Extras.Files
{
    public interface IManageExtraFiles
    {
        int Order { get; }
        Task<IEnumerable<ExtraFile>> CreateAfterMediaCoverUpdate(Series series);
        Task<IEnumerable<ExtraFile>> CreateAfterSeriesScan(Series series, List<EpisodeFile> episodeFiles);
        Task<IEnumerable<ExtraFile>> CreateAfterEpisodesImported(Series series);
        Task<IEnumerable<ExtraFile>> CreateAfterEpisodeImport(Series series, EpisodeFile episodeFile);
        Task<IEnumerable<ExtraFile>> CreateAfterEpisodeFolder(Series series, string seriesFolder, string seasonFolder);
        Task<IEnumerable<ExtraFile>> MoveFilesAfterRename(Series series, List<EpisodeFile> episodeFiles);
        bool CanImportFile(LocalEpisode localEpisode, EpisodeFile episodeFile, string path, string extension, bool readOnly);
        Task<IEnumerable<ExtraFile>> ImportFiles(LocalEpisode localEpisode, EpisodeFile episodeFile, List<string> files, bool isReadOnly);
    }

    public abstract class ExtraFileManager<TExtraFile> : IManageExtraFiles
        where TExtraFile : ExtraFile, new()
    {
        private readonly IConfigService _configService;
        private readonly IDiskProvider _diskProvider;
        private readonly IDiskTransferService _diskTransferService;
        private readonly Logger _logger;

        public ExtraFileManager(IConfigService configService,
                                IDiskProvider diskProvider,
                                IDiskTransferService diskTransferService,
                                Logger logger)
        {
            _configService = configService;
            _diskProvider = diskProvider;
            _diskTransferService = diskTransferService;
            _logger = logger;
        }

        public abstract int Order { get; }
        public abstract Task<IEnumerable<ExtraFile>> CreateAfterMediaCoverUpdate(Series series);
        public abstract Task<IEnumerable<ExtraFile>> CreateAfterSeriesScan(Series series, List<EpisodeFile> episodeFiles);
        public abstract Task<IEnumerable<ExtraFile>> CreateAfterEpisodesImported(Series series);
        public abstract Task<IEnumerable<ExtraFile>> CreateAfterEpisodeImport(Series series, EpisodeFile episodeFile);
        public abstract Task<IEnumerable<ExtraFile>> CreateAfterEpisodeFolder(Series series, string seriesFolder, string seasonFolder);
        public abstract Task<IEnumerable<ExtraFile>> MoveFilesAfterRename(Series series, List<EpisodeFile> episodeFiles);
        public abstract bool CanImportFile(LocalEpisode localEpisode, EpisodeFile episodeFile, string path, string extension, bool readOnly);
        public abstract Task<IEnumerable<ExtraFile>> ImportFiles(LocalEpisode localEpisode, EpisodeFile episodeFile, List<string> files, bool isReadOnly);

        protected TExtraFile ImportFile(Series series, EpisodeFile episodeFile, string path, bool readOnly, string extension, string fileNameSuffix = null)
        {
            var newFolder = Path.GetDirectoryName(Path.Combine(series.Path, episodeFile.RelativePath));
            var filenameBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(episodeFile.RelativePath));

            if (fileNameSuffix.IsNotNullOrWhiteSpace())
            {
                filenameBuilder.Append(fileNameSuffix);
            }

            filenameBuilder.Append(extension);

            var newFileName = Path.Combine(newFolder, filenameBuilder.ToString());
            var transferMode = TransferMode.Move;

            if (readOnly)
            {
                transferMode = _configService.CopyUsingHardlinks ? TransferMode.HardLinkOrCopy : TransferMode.Copy;
            }

            _diskTransferService.TransferFile(path, newFileName, transferMode, true);

            return new TExtraFile
            {
                SeriesId = series.Id,
                SeasonNumber = episodeFile.SeasonNumber,
                EpisodeFileId = episodeFile.Id,
                RelativePath = series.Path.GetRelativePath(newFileName),
                Extension = extension
            };
        }

        protected TExtraFile MoveFile(Series series, EpisodeFile episodeFile, TExtraFile extraFile, string fileNameSuffix = null)
        {
            _logger.Trace("Renaming extra file: {0}", extraFile);

            var newFolder = Path.GetDirectoryName(Path.Combine(series.Path, episodeFile.RelativePath));
            var filenameBuilder = new StringBuilder(Path.GetFileNameWithoutExtension(episodeFile.RelativePath));

            if (fileNameSuffix.IsNotNullOrWhiteSpace())
            {
                filenameBuilder.Append(fileNameSuffix);
            }

            filenameBuilder.Append(extraFile.Extension);

            var existingFileName = Path.Combine(series.Path, extraFile.RelativePath);
            var newFileName = Path.Combine(newFolder, filenameBuilder.ToString());

            if (newFileName.PathNotEquals(existingFileName))
            {
                try
                {
                    _logger.Trace("Renaming extra file: {0} to {1}", extraFile, newFileName);

                    _diskProvider.MoveFile(existingFileName, newFileName);
                    extraFile.RelativePath = series.Path.GetRelativePath(newFileName);

                    _logger.Trace("Renamed extra file from: {0}", extraFile);

                    return extraFile;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to move file after rename: {0}", existingFileName);
                }
            }

            return null;
        }
    }
}
