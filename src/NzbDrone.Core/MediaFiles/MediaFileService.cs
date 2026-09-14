using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileService
    {
        Task<EpisodeFile> Add(EpisodeFile episodeFile);
        Task Update(EpisodeFile episodeFile);
        Task Update(List<EpisodeFile> episodeFiles);
        Task Delete(EpisodeFile episodeFile, DeleteMediaFileReason reason);
        Task<List<EpisodeFile>> GetFilesBySeries(int seriesId);
        Task<List<EpisodeFile>> GetFilesBySeriesIds(List<int> seriesIds);
        Task<List<EpisodeFile>> GetFilesBySeason(int seriesId, int seasonNumber);
        Task<List<EpisodeFile>> GetFiles(IEnumerable<int> ids);
        Task<List<EpisodeFile>> GetFilesWithoutMediaInfo();
        Task<List<string>> FilterExistingFiles(List<string> files, Series series);
        Task<EpisodeFile> Get(int id);
        Task<List<EpisodeFile>> Get(IEnumerable<int> ids);
        Task<List<EpisodeFile>> GetFilesWithRelativePath(int seriesId, string relativePath);
    }

    public class MediaFileService : IMediaFileService, IHandleAsync<SeriesDeletedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly Logger _logger;

        public MediaFileService(IMediaFileRepository mediaFileRepository, IEventAggregator eventAggregator, Logger logger)
        {
            _mediaFileRepository = mediaFileRepository;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public async Task<EpisodeFile> Add(EpisodeFile episodeFile)
        {
            var addedFile = await _mediaFileRepository.Insert(episodeFile);
            _eventAggregator.PublishEvent(new EpisodeFileAddedEvent(addedFile));
            return addedFile;
        }

        public async Task Update(EpisodeFile episodeFile)
        {
            await _mediaFileRepository.Update(episodeFile);
        }

        public async Task Update(List<EpisodeFile> episodeFiles)
        {
            await _mediaFileRepository.UpdateMany(episodeFiles);
        }

        public async Task Delete(EpisodeFile episodeFile, DeleteMediaFileReason reason)
        {
            // Little hack so we have the episodes and series attached for the event consumers
            episodeFile.Episodes.LazyLoad();
            episodeFile.Path = Path.Combine(episodeFile.Series.Value.Path, episodeFile.RelativePath);

            await _mediaFileRepository.Delete(episodeFile);
            _eventAggregator.PublishEvent(new EpisodeFileDeletedEvent(episodeFile, reason));
        }

        public async Task<List<EpisodeFile>> GetFilesBySeries(int seriesId)
        {
            return await _mediaFileRepository.GetFilesBySeries(seriesId);
        }

        public async Task<List<EpisodeFile>> GetFilesBySeriesIds(List<int> seriesIds)
        {
            return await _mediaFileRepository.GetFilesBySeriesIds(seriesIds);
        }

        public async Task<List<EpisodeFile>> GetFilesBySeason(int seriesId, int seasonNumber)
        {
            return await _mediaFileRepository.GetFilesBySeason(seriesId, seasonNumber);
        }

        public async Task<List<EpisodeFile>> GetFiles(IEnumerable<int> ids)
        {
            return (await _mediaFileRepository.Get(ids)).ToList();
        }

        public async Task<List<EpisodeFile>> GetFilesWithoutMediaInfo()
        {
            return await _mediaFileRepository.GetFilesWithoutMediaInfo();
        }

        public async Task<List<string>> FilterExistingFiles(List<string> files, Series series)
        {
            var seriesFiles = await GetFilesBySeries(series.Id);

            return FilterExistingFiles(files, seriesFiles, series);
        }

        public async Task<EpisodeFile> Get(int id)
        {
            return await _mediaFileRepository.Get(id);
        }

        public async Task<List<EpisodeFile>> Get(IEnumerable<int> ids)
        {
            return (await _mediaFileRepository.Get(ids)).ToList();
        }

        public async Task<List<EpisodeFile>> GetFilesWithRelativePath(int seriesId, string relativePath)
        {
            return await _mediaFileRepository.GetFilesWithRelativePath(seriesId, relativePath);
        }

        // IHandleAsync<TEvent> is a shared eventing interface app-wide; its signature (void HandleAsync) can't
        // change here. Bridging with GetAwaiter().GetResult() is safe - EventAggregator runs handlers off the
        // request thread via Task.Factory.StartNew, and ASP.NET Core carries no SynchronizationContext.
        public void HandleAsync(SeriesDeletedEvent message)
        {
            _mediaFileRepository.DeleteForSeries(message.Series.Select(s => s.Id).ToList()).GetAwaiter().GetResult();
        }

        public static List<string> FilterExistingFiles(List<string> files, List<EpisodeFile> seriesFiles, Series series)
        {
            var seriesFilePaths = seriesFiles.Select(f => Path.Combine(series.Path, f.RelativePath)).ToList();

            if (!seriesFilePaths.Any())
            {
                return files;
            }

            return files.Except(seriesFilePaths, PathEqualityComparer.Instance).ToList();
        }
    }
}
