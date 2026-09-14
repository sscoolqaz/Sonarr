using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles
{
    public interface IRenameEpisodeFileService
    {
        Task<List<RenameEpisodeFilePreview>> GetRenamePreviews(int seriesId);
        Task<List<RenameEpisodeFilePreview>> GetRenamePreviews(int seriesId, int seasonNumber);
        Task<List<RenameEpisodeFilePreview>> GetRenamePreviews(List<int> seriesIds);
    }

    public class RenameEpisodeFileService : IRenameEpisodeFileService,
                                            IExecute<RenameFilesCommand>,
                                            IExecute<RenameSeriesCommand>
    {
        private readonly ISeriesService _seriesService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMoveEpisodeFiles _episodeFileMover;
        private readonly IEventAggregator _eventAggregator;
        private readonly IEpisodeService _episodeService;
        private readonly IBuildFileNames _filenameBuilder;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public RenameEpisodeFileService(ISeriesService seriesService,
                                        IMediaFileService mediaFileService,
                                        IMoveEpisodeFiles episodeFileMover,
                                        IEventAggregator eventAggregator,
                                        IEpisodeService episodeService,
                                        IBuildFileNames filenameBuilder,
                                        IDiskProvider diskProvider,
                                        Logger logger)
        {
            _seriesService = seriesService;
            _mediaFileService = mediaFileService;
            _episodeFileMover = episodeFileMover;
            _eventAggregator = eventAggregator;
            _episodeService = episodeService;
            _filenameBuilder = filenameBuilder;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public async Task<List<RenameEpisodeFilePreview>> GetRenamePreviews(int seriesId)
        {
            var series = await _seriesService.GetSeries(seriesId);
            var episodes = await _episodeService.GetEpisodeBySeries(seriesId);
            var files = await _mediaFileService.GetFilesBySeries(seriesId);

            return (await GetPreviews(series, episodes, files))
                .OrderByDescending(e => e.SeasonNumber)
                .ThenByDescending(e => e.EpisodeNumbers.First())
                .ToList();
        }

        public async Task<List<RenameEpisodeFilePreview>> GetRenamePreviews(int seriesId, int seasonNumber)
        {
            var series = await _seriesService.GetSeries(seriesId);
            var episodes = await _episodeService.GetEpisodesBySeason(seriesId, seasonNumber);
            var files = await _mediaFileService.GetFilesBySeason(seriesId, seasonNumber);

            return (await GetPreviews(series, episodes, files))
                .OrderByDescending(e => e.EpisodeNumbers.First()).ToList();
        }

        public async Task<List<RenameEpisodeFilePreview>> GetRenamePreviews(List<int> seriesIds)
        {
            var seriesList = await _seriesService.GetSeries(seriesIds);
            var episodesList = (await _episodeService.GetEpisodesBySeries(seriesIds)).ToLookup(e => e.SeriesId);
            var filesList = (await _mediaFileService.GetFilesBySeriesIds(seriesIds)).ToLookup(f => f.SeriesId);

            var result = new List<RenameEpisodeFilePreview>();

            foreach (var series in seriesList)
            {
                var episodes = episodesList[series.Id].ToList();
                var files = filesList[series.Id].ToList();

                result.AddRange(await GetPreviews(series, episodes, files));
            }

            return result
                .OrderByDescending(e => e.SeriesId)
                .ThenByDescending(e => e.SeasonNumber)
                .ThenByDescending(e => e.EpisodeNumbers.First())
                .ToList();
        }

        private async Task<List<RenameEpisodeFilePreview>> GetPreviews(Series series, List<Episode> episodes, List<EpisodeFile> files)
        {
            var result = new List<RenameEpisodeFilePreview>();

            foreach (var f in files)
            {
                var file = f;
                var episodesInFile = episodes.Where(e => e.EpisodeFileId == file.Id).ToList();
                var episodeFilePath = Path.Combine(series.Path, file.RelativePath);

                if (!episodesInFile.Any())
                {
                    _logger.Warn("File ({0}) is not linked to any episodes", episodeFilePath);
                    continue;
                }

                var seasonNumber = episodesInFile.First().SeasonNumber;
                var newPath = await _filenameBuilder.BuildFilePath(episodesInFile, series, file, Path.GetExtension(episodeFilePath));

                if (!episodeFilePath.PathEquals(newPath, StringComparison.Ordinal))
                {
                    result.Add(new RenameEpisodeFilePreview
                    {
                        SeriesId = series.Id,
                        SeasonNumber = seasonNumber,
                        EpisodeNumbers = episodesInFile.Select(e => e.EpisodeNumber).ToList(),
                        EpisodeFileId = file.Id,
                        ExistingPath = file.RelativePath,
                        NewPath = series.Path.GetRelativePath(newPath)
                    });
                }
            }

            return result;
        }

        private async Task<List<RenamedEpisodeFile>> RenameFiles(List<EpisodeFile> episodeFiles, Series series)
        {
            var renamed = new List<RenamedEpisodeFile>();

            foreach (var episodeFile in episodeFiles)
            {
                var previousRelativePath = episodeFile.RelativePath;
                var previousPath = Path.Combine(series.Path, episodeFile.RelativePath);

                try
                {
                    _logger.Debug("Renaming episode file: {0}", episodeFile);
                    await _episodeFileMover.MoveEpisodeFile(episodeFile, series);

                    await _mediaFileService.Update(episodeFile);

                    renamed.Add(new RenamedEpisodeFile
                                {
                                    EpisodeFile = episodeFile,
                                    PreviousRelativePath = previousRelativePath,
                                    PreviousPath = previousPath
                                });

                    _logger.Debug("Renamed episode file: {0}", episodeFile);

                    _eventAggregator.PublishEvent(new EpisodeFileRenamedEvent(series, episodeFile, previousPath));
                }
                catch (FileAlreadyExistsException ex)
                {
                    _logger.Warn("File not renamed, there is already a file at the destination: {0}", ex.Filename);
                }
                catch (SameFilenameException ex)
                {
                    _logger.Debug("File not renamed, source and destination are the same: {0}", ex.Filename);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to rename file {0}", previousPath);
                }
            }

            if (renamed.Any())
            {
                _diskProvider.RemoveEmptySubfolders(series.Path);

                _eventAggregator.PublishEvent(new SeriesRenamedEvent(series, renamed));
            }

            return renamed;
        }

        // IExecute<TCommand> is a shared, synchronous, app-wide command interface - its signature can't change
        // here. Bridging with GetAwaiter().GetResult() is safe for the same reason as the IHandle bridges
        // elsewhere this session (no SynchronizationContext on the threads command execution runs on).
        public void Execute(RenameFilesCommand message)
        {
            var series = _seriesService.GetSeries(message.SeriesId).GetAwaiter().GetResult();
            var episodeFiles = _mediaFileService.Get(message.Files).GetAwaiter().GetResult();

            _logger.ProgressInfo("Renaming {0} files for {1}", episodeFiles.Count, series.Title);
            var renamedFiles = RenameFiles(episodeFiles, series).GetAwaiter().GetResult();
            _logger.ProgressInfo("{0} selected episode files renamed for {1}", renamedFiles.Count, series.Title);

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }

        public void Execute(RenameSeriesCommand message)
        {
            _logger.Debug("Renaming all files for selected series");
            var seriesToRename = _seriesService.GetSeries(message.SeriesIds).GetAwaiter().GetResult();

            foreach (var series in seriesToRename)
            {
                var episodeFiles = _mediaFileService.GetFilesBySeries(series.Id).GetAwaiter().GetResult();
                _logger.ProgressInfo("Renaming all files in series: {0}", series.Title);
                var renamedFiles = RenameFiles(episodeFiles, series).GetAwaiter().GetResult();
                _logger.ProgressInfo("{0} episode files renamed for {1}", renamedFiles.Count, series.Title);
            }

            _eventAggregator.PublishEvent(new RenameCompletedEvent());
        }
    }
}
