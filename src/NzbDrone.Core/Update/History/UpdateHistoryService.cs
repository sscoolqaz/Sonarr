using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Update.History.Events;

namespace NzbDrone.Core.Update.History
{
    public interface IUpdateHistoryService
    {
        Task<Version> PreviouslyInstalled();
        Task<List<UpdateHistory>> InstalledSince(DateTime dateTime);
    }

    public class UpdateHistoryService : IUpdateHistoryService, IHandle<ApplicationStartedEvent>, IHandleAsync<ApplicationStartedEvent>
    {
        private readonly IUpdateHistoryRepository _repository;
        private readonly IEventAggregator _eventAggregator;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly Logger _logger;
        private Version _prevVersion;

        public UpdateHistoryService(IUpdateHistoryRepository repository, IEventAggregator eventAggregator, IConfigFileProvider configFileProvider, Logger logger)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public async Task<Version> PreviouslyInstalled()
        {
            try
            {
                var history = await _repository.PreviouslyInstalled();

                return history?.Version;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to determine previously installed version");
                return null;
            }
        }

        public async Task<List<UpdateHistory>> InstalledSince(DateTime dateTime)
        {
            try
            {
                return await _repository.InstalledSince(dateTime);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to get list of previously installed versions");
                return new List<UpdateHistory>();
            }
        }

        // NOTE: IHandle<TEvent>/IHandleAsync<TEvent> are shared eventing interfaces (50+/18+
        // implementers app-wide); their `void Handle(...)`/`void HandleAsync(...)` signatures are
        // out of scope to change. EventAggregator runs handlers off the request thread via
        // Task.Factory.StartNew and ASP.NET Core carries no SynchronizationContext, so bridging
        // here via GetAwaiter().GetResult() cannot deadlock.
        public void Handle(ApplicationStartedEvent message)
        {
            HandleApplicationStarted(message).GetAwaiter().GetResult();
        }

        private async Task HandleApplicationStarted(ApplicationStartedEvent message)
        {
            if (BuildInfo.Version.Major == 10 || !_configFileProvider.LogDbEnabled)
            {
                // Don't save dev versions, they change constantly
                return;
            }

            UpdateHistory history;
            try
            {
                history = await _repository.LastInstalled();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Cleaning corrupted update history");
                await _repository.Purge();
                history = null;
            }

            if (history == null || history.Version != BuildInfo.Version)
                {
                    _prevVersion = history?.Version;

                    await _repository.Insert(new UpdateHistory
                    {
                        Date = DateTime.UtcNow,
                        Version = BuildInfo.Version,
                        EventType = UpdateHistoryEventType.Installed
                    });
                }
        }

        public void HandleAsync(ApplicationStartedEvent message)
        {
            if (_prevVersion != null)
            {
                _eventAggregator.PublishEvent(new UpdateInstalledEvent(_prevVersion, BuildInfo.Version));
            }
        }
    }
}
