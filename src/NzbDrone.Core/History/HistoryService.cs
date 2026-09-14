using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.History
{
    public interface IHistoryService
    {
        Task<PagingSpec<EpisodeHistory>> Paged(PagingSpec<EpisodeHistory> pagingSpec, int[] languages, int[] qualities);
        Task<EpisodeHistory> MostRecentForEpisode(int episodeId);
        Task<List<EpisodeHistory>> FindByEpisodeId(int episodeId);
        Task<EpisodeHistory> MostRecentForDownloadId(string downloadId);
        Task<EpisodeHistory> Get(int historyId);
        Task<List<EpisodeHistory>> GetBySeries(int seriesId, EpisodeHistoryEventType? eventType);
        Task<List<EpisodeHistory>> GetBySeason(int seriesId, int seasonNumber, EpisodeHistoryEventType? eventType);
        Task<List<EpisodeHistory>> GetByEpisode(int episodeId, EpisodeHistoryEventType? eventType);
        Task<List<EpisodeHistory>> Find(string downloadId, EpisodeHistoryEventType eventType);
        Task<List<EpisodeHistory>> FindByDownloadId(string downloadId);
        Task<string> FindDownloadId(EpisodeImportedEvent trackedDownload);
        Task<List<EpisodeHistory>> Since(DateTime date, EpisodeHistoryEventType? eventType);
    }

    public class HistoryService : IHistoryService,
                                  IHandle<EpisodeGrabbedEvent>,
                                  IHandle<EpisodeImportedEvent>,
                                  IHandle<DownloadFailedEvent>,
                                  IHandle<EpisodeFileDeletedEvent>,
                                  IHandle<EpisodeFileRenamedEvent>,
                                  IHandle<SeriesDeletedEvent>,
                                  IHandle<DownloadIgnoredEvent>
    {
        private readonly IHistoryRepository _historyRepository;
        private readonly Logger _logger;

        public HistoryService(IHistoryRepository historyRepository, Logger logger)
        {
            _historyRepository = historyRepository;
            _logger = logger;
        }

        public Task<PagingSpec<EpisodeHistory>> Paged(PagingSpec<EpisodeHistory> pagingSpec, int[] languages, int[] qualities)
        {
            return _historyRepository.GetPaged(pagingSpec, languages, qualities);
        }

        public Task<EpisodeHistory> MostRecentForEpisode(int episodeId)
        {
            return _historyRepository.MostRecentForEpisode(episodeId);
        }

        public Task<List<EpisodeHistory>> FindByEpisodeId(int episodeId)
        {
            return _historyRepository.FindByEpisodeId(episodeId);
        }

        public Task<EpisodeHistory> MostRecentForDownloadId(string downloadId)
        {
            return _historyRepository.MostRecentForDownloadId(downloadId);
        }

        public Task<EpisodeHistory> Get(int historyId)
        {
            return _historyRepository.Get(historyId);
        }

        public Task<List<EpisodeHistory>> GetBySeries(int seriesId, EpisodeHistoryEventType? eventType)
        {
            return _historyRepository.GetBySeries(seriesId, eventType);
        }

        public Task<List<EpisodeHistory>> GetBySeason(int seriesId, int seasonNumber, EpisodeHistoryEventType? eventType)
        {
            return _historyRepository.GetBySeason(seriesId, seasonNumber, eventType);
        }

        public Task<List<EpisodeHistory>> GetByEpisode(int episodeId, EpisodeHistoryEventType? eventType)
        {
            return _historyRepository.GetByEpisode(episodeId, eventType);
        }

        public async Task<List<EpisodeHistory>> Find(string downloadId, EpisodeHistoryEventType eventType)
        {
            return (await _historyRepository.FindByDownloadId(downloadId)).Where(c => c.EventType == eventType).ToList();
        }

        public Task<List<EpisodeHistory>> FindByDownloadId(string downloadId)
        {
            return _historyRepository.FindByDownloadId(downloadId);
        }

        public async Task<string> FindDownloadId(EpisodeImportedEvent trackedDownload)
        {
            _logger.Debug("Trying to find downloadId for {0} from history", trackedDownload.ImportedEpisode.Path);

            var episodeIds = trackedDownload.EpisodeInfo.Episodes.Select(c => c.Id).ToList();
            var allHistory = await _historyRepository.FindDownloadHistory(trackedDownload.EpisodeInfo.Series.Id, trackedDownload.ImportedEpisode.Quality);

            // Find download related items for these episodes
            var episodesHistory = allHistory.Where(h => episodeIds.Contains(h.EpisodeId)).ToList();

            var processedDownloadId = episodesHistory
                .Where(c => c.EventType != EpisodeHistoryEventType.Grabbed && c.DownloadId != null)
                .Select(c => c.DownloadId);

            var stillDownloading = episodesHistory.Where(c => c.EventType == EpisodeHistoryEventType.Grabbed && !processedDownloadId.Contains(c.DownloadId)).ToList();

            string downloadId = null;

            if (stillDownloading.Any())
            {
                foreach (var matchingHistory in trackedDownload.EpisodeInfo.Episodes.Select(e => stillDownloading.Where(c => c.EpisodeId == e.Id).ToList()))
                {
                    if (matchingHistory.Count != 1)
                    {
                        return null;
                    }

                    var newDownloadId = matchingHistory.Single().DownloadId;

                    if (downloadId == null || downloadId == newDownloadId)
                    {
                        downloadId = newDownloadId;
                    }
                    else
                    {
                        return null;
                    }
                }
            }

            return downloadId;
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide); its
        // `void Handle(TEvent message)` signature is out of scope to change. EventAggregator runs
        // handlers off the request thread via Task.Factory.StartNew and ASP.NET Core carries no
        // SynchronizationContext, so bridging here via GetAwaiter().GetResult() cannot deadlock.
        public void Handle(EpisodeGrabbedEvent message)
        {
            HandleEpisodeGrabbed(message).GetAwaiter().GetResult();
        }

        private async Task HandleEpisodeGrabbed(EpisodeGrabbedEvent message)
        {
            foreach (var episode in message.Episode.Episodes)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.Grabbed,
                    Date = DateTime.UtcNow,
                    Quality = message.Episode.ParsedEpisodeInfo.Quality,
                    SourceTitle = message.Episode.Release.Title,
                    SeriesId = episode.SeriesId,
                    EpisodeId = episode.Id,
                    DownloadId = message.DownloadId,
                    Languages = message.Episode.Languages,
                };

                history.Data.Add("Indexer", message.Episode.Release.Indexer);
                history.Data.Add("NzbInfoUrl", message.Episode.Release.InfoUrl);
                history.Data.Add("ReleaseGroup", message.Episode.ParsedEpisodeInfo.ReleaseGroup);
                history.Data.Add("Age", message.Episode.Release.Age.ToString());
                history.Data.Add("AgeHours", message.Episode.Release.AgeHours.ToString());
                history.Data.Add("AgeMinutes", message.Episode.Release.AgeMinutes.ToString());
                history.Data.Add("PublishedDate", message.Episode.Release.PublishDate.ToUniversalTime().ToString("s") + "Z");
                history.Data.Add("DownloadClient", message.DownloadClient);
                history.Data.Add("DownloadClientName", message.DownloadClientName);
                history.Data.Add("Size", message.Episode.Release.Size.ToString());
                history.Data.Add("DownloadUrl", message.Episode.Release.DownloadUrl);
                history.Data.Add("Guid", message.Episode.Release.Guid);
                history.Data.Add("TvdbId", message.Episode.Release.TvdbId.ToString());
                history.Data.Add("TvRageId", message.Episode.Release.TvRageId.ToString());
                history.Data.Add("ImdbId", message.Episode.Release.ImdbId);
                history.Data.Add("Protocol", ((int)message.Episode.Release.DownloadProtocol).ToString());
                history.Data.Add("CustomFormatScore", message.Episode.CustomFormatScore.ToString());
                history.Data.Add("SeriesMatchType", message.Episode.SeriesMatchType.ToString());
                history.Data.Add("ReleaseSource", message.Episode.ReleaseSource.ToString());
                history.Data.Add("IndexerFlags", message.Episode.Release.IndexerFlags.ToString());
                history.Data.Add("ReleaseType", message.Episode.ParsedEpisodeInfo.ReleaseType.ToString());

                if (!message.Episode.ParsedEpisodeInfo.ReleaseHash.IsNullOrWhiteSpace())
                {
                    history.Data.Add("ReleaseHash", message.Episode.ParsedEpisodeInfo.ReleaseHash);
                }

                if (message.Episode.Release is TorrentInfo torrentRelease)
                {
                    history.Data.Add("TorrentInfoHash", torrentRelease.InfoHash);
                }

                await _historyRepository.Insert(history);
            }
        }

        public void Handle(EpisodeImportedEvent message)
        {
            HandleEpisodeImported(message).GetAwaiter().GetResult();
        }

        private async Task HandleEpisodeImported(EpisodeImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var downloadId = message.DownloadId;

            if (downloadId.IsNullOrWhiteSpace())
            {
                downloadId = await FindDownloadId(message);
            }

            foreach (var episode in message.EpisodeInfo.Episodes)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.DownloadFolderImported,
                    Date = DateTime.UtcNow,
                    Quality = message.EpisodeInfo.Quality,
                    SourceTitle = message.ImportedEpisode.SceneName ?? Path.GetFileNameWithoutExtension(message.EpisodeInfo.Path),
                    SeriesId = message.ImportedEpisode.SeriesId,
                    EpisodeId = episode.Id,
                    DownloadId = downloadId,
                    Languages = message.EpisodeInfo.Languages
                };

                history.Data.Add("FileId", message.ImportedEpisode.Id.ToString());
                history.Data.Add("DroppedPath", message.EpisodeInfo.Path);
                history.Data.Add("ImportedPath", Path.Combine(message.EpisodeInfo.Series.Path, message.ImportedEpisode.RelativePath));
                history.Data.Add("DownloadClient", message.DownloadClientInfo?.Type);
                history.Data.Add("DownloadClientName", message.DownloadClientInfo?.Name);
                history.Data.Add("ReleaseGroup", message.EpisodeInfo.ReleaseGroup);
                history.Data.Add("CustomFormatScore", message.EpisodeInfo.CustomFormatScore.ToString());
                history.Data.Add("Size", message.EpisodeInfo.Size.ToString());
                history.Data.Add("IndexerFlags", message.ImportedEpisode.IndexerFlags.ToString());
                history.Data.Add("ReleaseType", message.ImportedEpisode.ReleaseType.ToString());

                await _historyRepository.Insert(history);
            }
        }

        public void Handle(DownloadFailedEvent message)
        {
            HandleDownloadFailed(message).GetAwaiter().GetResult();
        }

        private async Task HandleDownloadFailed(DownloadFailedEvent message)
        {
            foreach (var episodeId in message.EpisodeIds)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.DownloadFailed,
                    Date = DateTime.UtcNow,
                    Quality = message.Quality,
                    SourceTitle = message.SourceTitle,
                    SeriesId = message.SeriesId,
                    EpisodeId = episodeId,
                    DownloadId = message.DownloadId,
                    Languages = message.Languages
                };

                history.Data.Add("DownloadClient", message.DownloadClient);
                history.Data.Add("DownloadClientName", message.TrackedDownload?.DownloadItem.DownloadClientInfo.Name);
                history.Data.Add("Message", message.Message);
                history.Data.Add("Source", message.Source);
                history.Data.Add("ReleaseGroup", message.TrackedDownload?.RemoteEpisode?.ParsedEpisodeInfo?.ReleaseGroup ?? message.Data.GetValueOrDefault(EpisodeHistory.RELEASE_GROUP));
                history.Data.Add("Size", message.TrackedDownload?.DownloadItem.TotalSize.ToString() ?? message.Data.GetValueOrDefault(EpisodeHistory.SIZE));
                history.Data.Add("Indexer", message.TrackedDownload?.RemoteEpisode?.Release?.Indexer ?? message.Data.GetValueOrDefault(EpisodeHistory.INDEXER));

                await _historyRepository.Insert(history);
            }
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            HandleEpisodeFileDeleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleEpisodeFileDeleted(EpisodeFileDeletedEvent message)
        {
            if (message.Reason == DeleteMediaFileReason.NoLinkedEpisodes)
            {
                _logger.Debug("Removing episode file from DB as part of cleanup routine, not creating history event.");
                return;
            }
            else if (message.Reason == DeleteMediaFileReason.ManualOverride)
            {
                _logger.Debug("Removing episode file from DB as part of manual override of existing file, not creating history event.");
                return;
            }

            foreach (var episode in message.EpisodeFile.Episodes.Value)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.EpisodeFileDeleted,
                    Date = DateTime.UtcNow,
                    Quality = message.EpisodeFile.Quality,
                    SourceTitle = message.EpisodeFile.Path,
                    SeriesId = message.EpisodeFile.SeriesId,
                    EpisodeId = episode.Id,
                    Languages = message.EpisodeFile.Languages
                };

                history.Data.Add("Reason", message.Reason.ToString());
                history.Data.Add("ReleaseGroup", message.EpisodeFile.ReleaseGroup);
                history.Data.Add("Size", message.EpisodeFile.Size.ToString());
                history.Data.Add("IndexerFlags", message.EpisodeFile.IndexerFlags.ToString());
                history.Data.Add("ReleaseType", message.EpisodeFile.ReleaseType.ToString());

                await _historyRepository.Insert(history);
            }
        }

        public void Handle(EpisodeFileRenamedEvent message)
        {
            HandleEpisodeFileRenamed(message).GetAwaiter().GetResult();
        }

        private async Task HandleEpisodeFileRenamed(EpisodeFileRenamedEvent message)
        {
            var sourcePath = message.OriginalPath;
            var sourceRelativePath = message.Series.Path.GetRelativePath(message.OriginalPath);
            var path = Path.Combine(message.Series.Path, message.EpisodeFile.RelativePath);
            var relativePath = message.EpisodeFile.RelativePath;

            foreach (var episode in message.EpisodeFile.Episodes.Value)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.EpisodeFileRenamed,
                    Date = DateTime.UtcNow,
                    Quality = message.EpisodeFile.Quality,
                    SourceTitle = message.OriginalPath,
                    SeriesId = message.EpisodeFile.SeriesId,
                    EpisodeId = episode.Id,
                    Languages = message.EpisodeFile.Languages
                };

                history.Data.Add("SourcePath", sourcePath);
                history.Data.Add("SourceRelativePath", sourceRelativePath);
                history.Data.Add("Path", path);
                history.Data.Add("RelativePath", relativePath);
                history.Data.Add("ReleaseGroup", message.EpisodeFile.ReleaseGroup);
                history.Data.Add("Size", message.EpisodeFile.Size.ToString());
                history.Data.Add("IndexerFlags", message.EpisodeFile.IndexerFlags.ToString());
                history.Data.Add("ReleaseType", message.EpisodeFile.ReleaseType.ToString());

                await _historyRepository.Insert(history);
            }
        }

        public void Handle(DownloadIgnoredEvent message)
        {
            HandleDownloadIgnored(message).GetAwaiter().GetResult();
        }

        private async Task HandleDownloadIgnored(DownloadIgnoredEvent message)
        {
            var historyToAdd = new List<EpisodeHistory>();

            foreach (var episodeId in message.EpisodeIds)
            {
                var history = new EpisodeHistory
                {
                    EventType = EpisodeHistoryEventType.DownloadIgnored,
                    Date = DateTime.UtcNow,
                    Quality = message.Quality,
                    SourceTitle = message.SourceTitle,
                    SeriesId = message.SeriesId,
                    EpisodeId = episodeId,
                    DownloadId = message.DownloadId,
                    Languages = message.Languages
                };

                history.Data.Add("DownloadClient", message.DownloadClientInfo.Type);
                history.Data.Add("DownloadClientName", message.DownloadClientInfo.Name);
                history.Data.Add("Message", message.Message);
                history.Data.Add("ReleaseGroup", message.TrackedDownload?.RemoteEpisode?.ParsedEpisodeInfo?.ReleaseGroup);
                history.Data.Add("Size", message.TrackedDownload?.DownloadItem.TotalSize.ToString());
                history.Data.Add("Indexer", message.TrackedDownload?.RemoteEpisode?.Release?.Indexer);
                history.Data.Add("ReleaseType", message.TrackedDownload?.RemoteEpisode?.ParsedEpisodeInfo?.ReleaseType.ToString());

                historyToAdd.Add(history);
            }

            await _historyRepository.InsertMany(historyToAdd);
        }

        public void Handle(SeriesDeletedEvent message)
        {
            HandleSeriesDeleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleSeriesDeleted(SeriesDeletedEvent message)
        {
            await _historyRepository.DeleteForSeries(message.Series.Select(m => m.Id).ToList());
        }

        public Task<List<EpisodeHistory>> Since(DateTime date, EpisodeHistoryEventType? eventType)
        {
            return _historyRepository.Since(date, eventType);
        }
    }
}
