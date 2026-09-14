using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Core.Update.History.Events;

namespace NzbDrone.Core.Notifications
{
    public class NotificationService
        : IHandle<EpisodeGrabbedEvent>,
          IHandle<EpisodeImportedEvent>,
          IHandle<DownloadCompletedEvent>,
          IHandle<UntrackedDownloadCompletedEvent>,
          IHandle<SeriesRenamedEvent>,
          IHandle<SeriesAddCompletedEvent>,
          IHandle<SeriesDeletedEvent>,
          IHandle<EpisodeFileDeletedEvent>,
          IHandle<HealthCheckFailedEvent>,
          IHandle<HealthCheckRestoredEvent>,
          IHandle<UpdateInstalledEvent>,
          IHandle<ManualInteractionRequiredEvent>,
          IHandleAsync<DeleteCompletedEvent>,
          IHandleAsync<DownloadsProcessedEvent>,
          IHandleAsync<RenameCompletedEvent>,
          IHandleAsync<HealthCheckCompleteEvent>
    {
        private readonly INotificationFactory _notificationFactory;
        private readonly INotificationStatusService _notificationStatusService;
        private readonly Logger _logger;

        public NotificationService(INotificationFactory notificationFactory, INotificationStatusService notificationStatusService, Logger logger)
        {
            _notificationFactory = notificationFactory;
            _notificationStatusService = notificationStatusService;
            _logger = logger;
        }

        private string GetMessage(Series series, List<Episode> episodes, QualityModel quality)
        {
            var qualityString = GetQualityString(series, quality);

            if (episodes.Empty())
            {
                return $"{series.Title} - [{qualityString}]";
            }

            if (series.SeriesType == SeriesTypes.Daily)
            {
                var episode = episodes.First();

                return $"{series.Title} - {episode.AirDate} - {episode.Title} [{qualityString}]";
            }

            var episodeNumbers = string.Concat(episodes.Select(e => $"x{e.EpisodeNumber:00}"));

            var episodeTitles = string.Join(" + ", episodes.Select(e => e.Title));

            return $"{series.Title} - {episodes.First().SeasonNumber}{episodeNumbers} - {episodeTitles} [{qualityString}]";
        }

        private string GetFullSeasonMessage(Series series, int seasonNumber, QualityModel quality)
        {
            var qualityString = GetQualityString(series, quality);

            return $"{series.Title} - Season {seasonNumber} [{qualityString}]";
        }

        private string GetQualityString(Series series, QualityModel quality)
        {
            var qualityString = quality.Quality.ToString();

            if (quality.Revision.Version > 1)
            {
                if (series.SeriesType == SeriesTypes.Anime)
                {
                    qualityString += " v" + quality.Revision.Version;
                }
                else
                {
                    qualityString += " Proper";
                }
            }

            return qualityString;
        }

        private bool ShouldHandleSeries(ProviderDefinition definition, Series series)
        {
            if (definition.Tags.Empty())
            {
                _logger.Debug("No tags set for this notification.");
                return true;
            }

            if (definition.Tags.Intersect(series.Tags).Any())
            {
                _logger.Debug("Notification and series have one or more intersecting tags.");
                return true;
            }

            _logger.Debug("{0} does not have any intersecting tags with {1}. Notification will not be sent.", definition.Name, series.Title);
            return false;
        }

        private bool ShouldHandleHealthFailure(HealthCheck.HealthCheck healthCheck, bool includeWarnings)
        {
            if (healthCheck.Type == HealthCheckResult.Error)
            {
                return true;
            }

            if (healthCheck.Type == HealthCheckResult.Warning && includeWarnings)
            {
                return true;
            }

            return false;
        }

        // NOTE: IHandle<TEvent>/IHandleAsync<TEvent> are shared eventing interfaces (50+/18+
        // implementers app-wide); their `void Handle(...)`/`void HandleAsync(...)` signatures are
        // out of scope to change. EventAggregator runs handlers off the request thread via
        // Task.Factory.StartNew and ASP.NET Core carries no SynchronizationContext, so bridging
        // here via GetAwaiter().GetResult() cannot deadlock.
        public void Handle(EpisodeGrabbedEvent message)
        {
            HandleEpisodeGrabbed(message).GetAwaiter().GetResult();
        }

        private async Task HandleEpisodeGrabbed(EpisodeGrabbedEvent message)
        {
            var grabMessage = new GrabMessage
            {
                Message = GetMessage(message.Episode.Series, message.Episode.Episodes, message.Episode.ParsedEpisodeInfo.Quality),
                Series = message.Episode.Series,
                Quality = message.Episode.ParsedEpisodeInfo.Quality,
                Episode = message.Episode,
                DownloadClientType = message.DownloadClient,
                DownloadClientName = message.DownloadClientName,
                DownloadId = message.DownloadId
            };

            foreach (var notification in await _notificationFactory.OnGrabEnabled())
            {
                try
                {
                    if (!ShouldHandleSeries(notification.Definition, message.Episode.Series))
                    {
                        continue;
                    }

                    notification.OnGrab(grabMessage);
                    await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Error(ex, "Unable to send OnGrab notification to {0}", notification.Definition.Name);
                }
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

            var downloadMessage = new DownloadMessage
            {
                Message = GetMessage(message.EpisodeInfo.Series, message.EpisodeInfo.Episodes, message.EpisodeInfo.Quality),
                Series = message.EpisodeInfo.Series,
                EpisodeInfo = message.EpisodeInfo,
                EpisodeFile = message.ImportedEpisode,
                OldFiles = message.OldFiles,
                SourcePath = message.EpisodeInfo.Path,
                DownloadClientInfo = message.DownloadClientInfo,
                DownloadId = message.DownloadId,
                Release = message.EpisodeInfo.Release
            };

            foreach (var notification in await _notificationFactory.OnDownloadEnabled())
            {
                try
                {
                    if (ShouldHandleSeries(notification.Definition, message.EpisodeInfo.Series))
                    {
                        if (downloadMessage.OldFiles.Empty() || ((NotificationDefinition)notification.Definition).OnUpgrade)
                        {
                            notification.OnDownload(downloadMessage);
                            await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnDownload notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(DownloadCompletedEvent message)
        {
            HandleDownloadCompleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleDownloadCompleted(DownloadCompletedEvent message)
        {
            var series = message.TrackedDownload.RemoteEpisode.Series;
            var episodes = message.TrackedDownload.RemoteEpisode.Episodes;
            var parsedEpisodeInfo = message.TrackedDownload.RemoteEpisode.ParsedEpisodeInfo;

            var downloadMessage = new ImportCompleteMessage
            {
                Message = parsedEpisodeInfo.FullSeason
                    ? GetFullSeasonMessage(series, episodes.First().SeasonNumber, parsedEpisodeInfo.Quality)
                    : GetMessage(series, episodes, parsedEpisodeInfo.Quality),
                Series = series,
                Episodes = episodes,
                EpisodeFiles = message.EpisodeFiles,
                DownloadClientInfo = message.TrackedDownload.DownloadItem.DownloadClientInfo,
                DownloadId = message.TrackedDownload.DownloadItem.DownloadId,
                Release = message.Release,
                SourcePath = message.TrackedDownload.DownloadItem.OutputPath.FullPath,
                DestinationPath = message.EpisodeFiles.Select(e => Path.Join(series.Path, e.RelativePath)).ToList().GetLongestCommonPath(),
                ReleaseGroup = parsedEpisodeInfo.ReleaseGroup,
                ReleaseQuality = parsedEpisodeInfo.Quality
            };

            foreach (var notification in await _notificationFactory.OnImportCompleteEnabled())
            {
                try
                {
                    if (ShouldHandleSeries(notification.Definition, series))
                    {
                        if (((NotificationDefinition)notification.Definition).OnImportComplete)
                        {
                            notification.OnImportComplete(downloadMessage);
                            await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnImportComplete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(UntrackedDownloadCompletedEvent message)
        {
            HandleUntrackedDownloadCompleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleUntrackedDownloadCompleted(UntrackedDownloadCompletedEvent message)
        {
            var series = message.Series;
            var episodes = message.Episodes;
            var parsedEpisodeInfo = message.ParsedEpisodeInfo;

            var downloadMessage = new ImportCompleteMessage
            {
                Message = parsedEpisodeInfo.FullSeason
                    ? GetFullSeasonMessage(series, episodes.First().SeasonNumber, parsedEpisodeInfo.Quality)
                    : GetMessage(series, episodes, parsedEpisodeInfo.Quality),
                Series = series,
                Episodes = episodes,
                EpisodeFiles = message.EpisodeFiles,
                SourcePath = message.SourcePath,
                SourceTitle = parsedEpisodeInfo.ReleaseTitle,
                DestinationPath = message.EpisodeFiles.Select(e => Path.Join(series.Path, e.RelativePath)).ToList().GetLongestCommonPath(),
                ReleaseGroup = parsedEpisodeInfo.ReleaseGroup,
                ReleaseQuality = parsedEpisodeInfo.Quality
            };

            foreach (var notification in await _notificationFactory.OnImportCompleteEnabled())
            {
                try
                {
                    if (ShouldHandleSeries(notification.Definition, series))
                    {
                        if (((NotificationDefinition)notification.Definition).OnImportComplete)
                        {
                            notification.OnImportComplete(downloadMessage);
                            await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnImportComplete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(SeriesRenamedEvent message)
        {
            HandleSeriesRenamed(message).GetAwaiter().GetResult();
        }

        private async Task HandleSeriesRenamed(SeriesRenamedEvent message)
        {
            foreach (var notification in await _notificationFactory.OnRenameEnabled())
            {
                try
                {
                    if (ShouldHandleSeries(notification.Definition, message.Series))
                    {
                        notification.OnRename(message.Series, message.RenamedFiles);
                        await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnRename notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(UpdateInstalledEvent message)
        {
            HandleUpdateInstalled(message).GetAwaiter().GetResult();
        }

        private async Task HandleUpdateInstalled(UpdateInstalledEvent message)
        {
            var updateMessage = new ApplicationUpdateMessage();
            updateMessage.Message = $"Sonarr updated from {message.PreviousVerison.ToString()} to {message.NewVersion.ToString()}";
            updateMessage.PreviousVersion = message.PreviousVerison;
            updateMessage.NewVersion = message.NewVersion;

            foreach (var notification in await _notificationFactory.OnApplicationUpdateEnabled())
            {
                try
                {
                    notification.OnApplicationUpdate(updateMessage);
                    await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnApplicationUpdate notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(ManualInteractionRequiredEvent message)
        {
            HandleManualInteractionRequired(message).GetAwaiter().GetResult();
        }

        private async Task HandleManualInteractionRequired(ManualInteractionRequiredEvent message)
        {
            var series = message.Episode?.Series;
            var mess = "";

            if (series != null)
            {
                mess = GetMessage(series, message.Episode.Episodes, message.Episode.ParsedEpisodeInfo.Quality);
            }

            if (mess.IsNullOrWhiteSpace() && message.TrackedDownload.DownloadItem != null)
            {
                mess = message.TrackedDownload.DownloadItem.Title;
            }

            if (mess.IsNullOrWhiteSpace())
            {
                return;
            }

            var manualInteractionMessage = new ManualInteractionRequiredMessage
            {
                Message = mess,
                Series = series,
                Quality = message.Episode?.ParsedEpisodeInfo.Quality,
                Episode = message.Episode,
                TrackedDownload = message.TrackedDownload,
                DownloadClientInfo = message.TrackedDownload.DownloadItem?.DownloadClientInfo,
                DownloadId = message.TrackedDownload.DownloadItem?.DownloadId,
                Release = message.Release
            };

            foreach (var notification in await _notificationFactory.OnManualInteractionEnabled())
            {
                try
                {
                    if (!ShouldHandleSeries(notification.Definition, message.Episode.Series))
                    {
                        continue;
                    }

                    notification.OnManualInteractionRequired(manualInteractionMessage);
                    await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Error(ex, "Unable to send OnManualInteractionRequired notification to {0}", notification.Definition.Name);
                }
            }
        }

        public void Handle(EpisodeFileDeletedEvent message)
        {
            HandleEpisodeFileDeleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleEpisodeFileDeleted(EpisodeFileDeletedEvent message)
        {
            if (message.EpisodeFile.Episodes.Value.Empty())
            {
                _logger.Trace("Skipping notification for deleted file without an episode (episode metadata was removed)");

                return;
            }

            var deleteMessage = new EpisodeDeleteMessage();
            deleteMessage.Message = GetMessage(message.EpisodeFile.Series, message.EpisodeFile.Episodes, message.EpisodeFile.Quality);
            deleteMessage.Series = message.EpisodeFile.Series;
            deleteMessage.EpisodeFile = message.EpisodeFile;
            deleteMessage.Reason = message.Reason;

            foreach (var notification in await _notificationFactory.OnEpisodeFileDeleteEnabled())
            {
                try
                {
                    if (message.Reason != MediaFiles.DeleteMediaFileReason.Upgrade || ((NotificationDefinition)notification.Definition).OnEpisodeFileDeleteForUpgrade)
                    {
                        if (ShouldHandleSeries(notification.Definition, deleteMessage.EpisodeFile.Series))
                        {
                            notification.OnEpisodeFileDelete(deleteMessage);
                            await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnEpisodeFileDelete notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(SeriesAddCompletedEvent message)
        {
            HandleSeriesAddCompleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleSeriesAddCompleted(SeriesAddCompletedEvent message)
        {
            var series = message.Series;
            var addMessage = new SeriesAddMessage
            {
                Series = series,
                Message = series.Title
            };

            foreach (var notification in await _notificationFactory.OnSeriesAddEnabled())
            {
                try
                {
                    if (ShouldHandleSeries(notification.Definition, series))
                    {
                        notification.OnSeriesAdd(addMessage);
                        await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnSeriesAdd notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(SeriesDeletedEvent message)
        {
            HandleSeriesDeleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleSeriesDeleted(SeriesDeletedEvent message)
        {
            foreach (var series in message.Series)
            {
                var deleteMessage = new SeriesDeleteMessage(series, message.DeleteFiles);

                foreach (var notification in await _notificationFactory.OnSeriesDeleteEnabled())
                {
                    try
                    {
                        if (ShouldHandleSeries(notification.Definition, deleteMessage.Series))
                        {
                            notification.OnSeriesDelete(deleteMessage);
                            await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        await _notificationStatusService.RecordFailure(notification.Definition.Id);
                        _logger.Warn(ex, "Unable to send OnSeriesDelete notification to: " + notification.Definition.Name);
                    }
                }
            }
        }

        public void Handle(HealthCheckFailedEvent message)
        {
            HandleHealthCheckFailed(message).GetAwaiter().GetResult();
        }

        private async Task HandleHealthCheckFailed(HealthCheckFailedEvent message)
        {
            // Don't send health check notifications during the start up grace period,
            // once that duration expires they they'll be retested and fired off if necessary.

            if (message.IsInStartupGracePeriod)
            {
                return;
            }

            foreach (var notification in await _notificationFactory.OnHealthIssueEnabled())
            {
                try
                {
                    if (ShouldHandleHealthFailure(message.HealthCheck, ((NotificationDefinition)notification.Definition).IncludeHealthWarnings))
                    {
                        notification.OnHealthIssue(message.HealthCheck);
                        await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnHealthIssue notification to: " + notification.Definition.Name);
                }
            }
        }

        public void Handle(HealthCheckRestoredEvent message)
        {
            HandleHealthCheckRestored(message).GetAwaiter().GetResult();
        }

        private async Task HandleHealthCheckRestored(HealthCheckRestoredEvent message)
        {
            if (message.IsInStartupGracePeriod)
            {
                return;
            }

            foreach (var notification in await _notificationFactory.OnHealthRestoredEnabled())
            {
                try
                {
                    if (ShouldHandleHealthFailure(message.PreviousCheck, ((NotificationDefinition)notification.Definition).IncludeHealthWarnings))
                    {
                        notification.OnHealthRestored(message.PreviousCheck);
                        await _notificationStatusService.RecordSuccess(notification.Definition.Id);
                    }
                }
                catch (Exception ex)
                {
                    await _notificationStatusService.RecordFailure(notification.Definition.Id);
                    _logger.Warn(ex, "Unable to send OnHealthRestored notification to: " + notification.Definition.Name);
                }
            }
        }

        public void HandleAsync(DeleteCompletedEvent message)
        {
            ProcessQueue().GetAwaiter().GetResult();
        }

        public void HandleAsync(DownloadsProcessedEvent message)
        {
            ProcessQueue().GetAwaiter().GetResult();
        }

        public void HandleAsync(RenameCompletedEvent message)
        {
            ProcessQueue().GetAwaiter().GetResult();
        }

        public void HandleAsync(HealthCheckCompleteEvent message)
        {
            ProcessQueue().GetAwaiter().GetResult();
        }

        private async Task ProcessQueue()
        {
            foreach (var notification in await _notificationFactory.GetAvailableProviders())
            {
                try
                {
                    notification.ProcessQueue();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to process notification queue for " + notification.Definition.Name);
                }
            }
        }
    }
}
