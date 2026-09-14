using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Extras
{
    public interface IExtraService
    {
        Task MoveFilesAfterRename(Series series, EpisodeFile episodeFile);
        Task ImportEpisode(LocalEpisode localEpisode, EpisodeFile episodeFile, bool isReadOnly);
    }

    public class ExtraService : IExtraService,
                                IHandle<MediaCoversUpdatedEvent>,
                                IHandle<EpisodeFolderCreatedEvent>,
                                IHandle<SeriesScannedEvent>,
                                IHandle<SeriesRenamedEvent>,
                                IHandle<DownloadsProcessedEvent>
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly IEpisodeService _episodeService;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly List<IManageExtraFiles> _extraFileManagers;
        private readonly Dictionary<int, Series> _seriesWithImportedFiles;

        public ExtraService(IMediaFileService mediaFileService,
                            IEpisodeService episodeService,
                            IDiskProvider diskProvider,
                            IConfigService configService,
                            IEnumerable<IManageExtraFiles> extraFileManagers,
                            Logger logger)
        {
            _mediaFileService = mediaFileService;
            _episodeService = episodeService;
            _diskProvider = diskProvider;
            _configService = configService;
            _extraFileManagers = extraFileManagers.OrderBy(e => e.Order).ToList();
            _seriesWithImportedFiles = new Dictionary<int, Series>();
        }

        public async Task ImportEpisode(LocalEpisode localEpisode, EpisodeFile episodeFile, bool isReadOnly)
        {
            await ImportExtraFiles(localEpisode, episodeFile, isReadOnly);

            await CreateAfterEpisodeImport(localEpisode.Series, episodeFile);
        }

        private async Task ImportExtraFiles(LocalEpisode localEpisode, EpisodeFile episodeFile, bool isReadOnly)
        {
            if (!_configService.ImportExtraFiles)
            {
                return;
            }

            var folderSearchOption = localEpisode.FolderEpisodeInfo != null;

            var wantedExtensions = _configService.ExtraFileExtensions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                                     .Select(e => e.Trim(' ', '.')
                                                                     .Insert(0, "."))
                                                                     .ToList();

            var sourceFolder = _diskProvider.GetParentFolder(localEpisode.Path);
            var files = _diskProvider.GetFiles(sourceFolder, folderSearchOption);
            var managedFiles = _extraFileManagers.Select((i) => new List<string>()).ToArray();

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                var matchingExtension = wantedExtensions.FirstOrDefault(e => e.Equals(extension));

                if (matchingExtension == null)
                {
                    continue;
                }

                for (var i = 0; i < _extraFileManagers.Count; i++)
                {
                    if (_extraFileManagers[i].CanImportFile(localEpisode, episodeFile, file, extension, isReadOnly))
                    {
                        managedFiles[i].Add(file);
                        break;
                    }
                }
            }

            for (var i = 0; i < _extraFileManagers.Count; i++)
            {
                await _extraFileManagers[i].ImportFiles(localEpisode, episodeFile, managedFiles[i], isReadOnly);
            }
        }

        private async Task CreateAfterEpisodeImport(Series series, EpisodeFile episodeFile)
        {
            lock (_seriesWithImportedFiles)
            {
                _seriesWithImportedFiles.TryAdd(series.Id, series);
            }

            foreach (var extraFileManager in _extraFileManagers)
            {
                await extraFileManager.CreateAfterEpisodeImport(series, episodeFile);
            }
        }

        // IHandle<T> is a shared, synchronous, app-wide eventing interface - its signature can't change here.
        // Bridging with GetAwaiter().GetResult() is safe: EventAggregator runs handlers off the request thread
        // via Task.Factory.StartNew, and ASP.NET Core carries no SynchronizationContext.
        public void Handle(MediaCoversUpdatedEvent message)
        {
            if (message.Updated)
            {
                var series = message.Series;

                foreach (var extraFileManager in _extraFileManagers)
                {
                    extraFileManager.CreateAfterMediaCoverUpdate(series).GetAwaiter().GetResult();
                }
            }
        }

        public void Handle(SeriesScannedEvent message)
        {
            var series = message.Series;
            var episodeFiles = GetEpisodeFiles(series.Id).GetAwaiter().GetResult();

            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.CreateAfterSeriesScan(series, episodeFiles).GetAwaiter().GetResult();
            }
        }

        public void Handle(EpisodeFolderCreatedEvent message)
        {
            var series = message.Series;

            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.CreateAfterEpisodeFolder(series, message.SeriesFolder, message.SeasonFolder).GetAwaiter().GetResult();
            }
        }

        public async Task MoveFilesAfterRename(Series series, EpisodeFile episodeFile)
        {
            var episodeFiles = new List<EpisodeFile> { episodeFile };

            foreach (var extraFileManager in _extraFileManagers)
            {
                await extraFileManager.MoveFilesAfterRename(series, episodeFiles);
            }
        }

        public void Handle(SeriesRenamedEvent message)
        {
            var series = message.Series;
            var episodeFiles = GetEpisodeFiles(series.Id).GetAwaiter().GetResult();

            foreach (var extraFileManager in _extraFileManagers)
            {
                extraFileManager.MoveFilesAfterRename(series, episodeFiles).GetAwaiter().GetResult();
            }
        }

        public void Handle(DownloadsProcessedEvent message)
        {
            var allSeries = new List<Series>();

            lock (_seriesWithImportedFiles)
            {
                allSeries.AddRange(_seriesWithImportedFiles.Values);

                _seriesWithImportedFiles.Clear();
            }

            foreach (var series in allSeries)
            {
                foreach (var extraFileManager in _extraFileManagers)
                {
                    extraFileManager.CreateAfterEpisodesImported(series).GetAwaiter().GetResult();
                }
            }
        }

        private async Task<List<EpisodeFile>> GetEpisodeFiles(int seriesId)
        {
            var episodeFiles = await _mediaFileService.GetFilesBySeries(seriesId);
            var episodes = await _episodeService.GetEpisodeBySeries(seriesId);

            foreach (var episodeFile in episodeFiles)
            {
                var localEpisodeFile = episodeFile;
                episodeFile.Episodes = new List<Episode>(episodes.Where(e => e.EpisodeFileId == localEpisodeFile.Id));
            }

            return episodeFiles;
        }
    }
}
