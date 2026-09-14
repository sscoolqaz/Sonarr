using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Extras.Files
{
    public interface IExtraFileService<TExtraFile>
        where TExtraFile : ExtraFile, new()
    {
        Task<List<TExtraFile>> GetFilesBySeries(int seriesId);
        Task<List<TExtraFile>> GetFilesByEpisodeFile(int episodeFileId);
        Task<TExtraFile> FindByPath(int seriesId, string path);
        Task Upsert(TExtraFile extraFile);
        Task Upsert(List<TExtraFile> extraFiles);
        Task Delete(int id);
        Task DeleteMany(IEnumerable<int> ids);
    }

    public abstract class ExtraFileService<TExtraFile> : IExtraFileService<TExtraFile>,
                                                         IHandleAsync<SeriesDeletedEvent>,
                                                         IHandle<EpisodeFileDeletedEvent>
        where TExtraFile : ExtraFile, new()
    {
        private readonly IExtraFileRepository<TExtraFile> _repository;
        private readonly ISeriesService _seriesService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly Logger _logger;

        public ExtraFileService(IExtraFileRepository<TExtraFile> repository,
                                ISeriesService seriesService,
                                IDiskProvider diskProvider,
                                IRecycleBinProvider recycleBinProvider,
                                Logger logger)
        {
            _repository = repository;
            _seriesService = seriesService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _logger = logger;
        }

        public async Task<List<TExtraFile>> GetFilesBySeries(int seriesId)
        {
            return await _repository.GetFilesBySeries(seriesId);
        }

        public async Task<List<TExtraFile>> GetFilesByEpisodeFile(int episodeFileId)
        {
            return await _repository.GetFilesByEpisodeFile(episodeFileId);
        }

        public async Task<TExtraFile> FindByPath(int seriesId, string path)
        {
            return await _repository.FindByPath(seriesId, path);
        }

        public async Task Upsert(TExtraFile extraFile)
        {
            await Upsert(new List<TExtraFile> { extraFile });
        }

        public async Task Upsert(List<TExtraFile> extraFiles)
        {
            extraFiles.ForEach(m =>
            {
                m.LastUpdated = DateTime.UtcNow;

                if (m.Id == 0)
                {
                    m.Added = m.LastUpdated;
                }
            });

            await _repository.InsertMany(extraFiles.Where(m => m.Id == 0).ToList());
            await _repository.UpdateMany(extraFiles.Where(m => m.Id > 0).ToList());
        }

        public async Task Delete(int id)
        {
            await _repository.Delete(id);
        }

        public async Task DeleteMany(IEnumerable<int> ids)
        {
            await _repository.DeleteMany(ids);
        }

        // IHandleAsync<TEvent> is a shared eventing interface app-wide; its signature (void HandleAsync) can't
        // change here. Bridging with GetAwaiter().GetResult() is safe - EventAggregator runs handlers off the
        // request thread via Task.Factory.StartNew, and ASP.NET Core carries no SynchronizationContext.
        public void HandleAsync(SeriesDeletedEvent message)
        {
            _logger.Debug("Deleting Extra from database for series: {0}", string.Join(',', message.Series));
            _repository.DeleteForSeriesIds(message.Series.Select(m => m.Id).ToList()).GetAwaiter().GetResult();
        }

        // IHandle<TEvent> is a shared eventing interface app-wide; its signature can't change here.
        // Bridging with GetAwaiter().GetResult() is safe for the same reason as above.
        public void Handle(EpisodeFileDeletedEvent message)
        {
            var episodeFile = message.EpisodeFile;

            if (message.Reason == DeleteMediaFileReason.NoLinkedEpisodes)
            {
                _logger.Debug("Removing episode file from DB as part of cleanup routine, not deleting extra files from disk.");
            }
            else
            {
                var series = _seriesService.GetSeries(message.EpisodeFile.SeriesId).GetAwaiter().GetResult();

                foreach (var extra in _repository.GetFilesByEpisodeFile(episodeFile.Id).GetAwaiter().GetResult())
                {
                    var path = Path.Combine(series.Path, extra.RelativePath);

                    if (_diskProvider.FileExists(path))
                    {
                        // Send to the recycling bin so they can be recovered if necessary
                        var subfolder = _diskProvider.GetParentFolder(series.Path).GetRelativePath(_diskProvider.GetParentFolder(path));
                        _recycleBinProvider.DeleteFile(path, subfolder);
                    }
                }
            }

            _logger.Debug("Deleting Extra from database for episode file: {0}", episodeFile);
            _repository.DeleteForEpisodeFile(episodeFile.Id).GetAwaiter().GetResult();
        }
    }
}
