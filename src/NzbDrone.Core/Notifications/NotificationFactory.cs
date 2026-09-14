using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    public interface INotificationFactory : IProviderFactory<INotification, NotificationDefinition>
    {
        Task<List<INotification>> OnGrabEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnDownloadEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnUpgradeEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnImportCompleteEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnRenameEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnSeriesAddEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnSeriesDeleteEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnEpisodeFileDeleteEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnEpisodeFileDeleteForUpgradeEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnHealthIssueEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnHealthRestoredEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnApplicationUpdateEnabled(bool filterBlockedNotifications = true);
        Task<List<INotification>> OnManualInteractionEnabled(bool filterBlockedNotifications = true);
    }

    public class NotificationFactory : ProviderFactory<INotification, NotificationDefinition>, INotificationFactory
    {
        private readonly INotificationStatusService _notificationStatusService;
        private readonly Logger _logger;

        public NotificationFactory(INotificationStatusService notificationStatusService, INotificationRepository providerRepository, IEnumerable<INotification> providers, IServiceProvider container, IEventAggregator eventAggregator, Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _notificationStatusService = notificationStatusService;
            _logger = logger;
        }

        protected override async Task<List<NotificationDefinition>> Active()
        {
            return (await base.Active()).Where(c => c.Enable).ToList();
        }

        public async Task<List<INotification>> OnGrabEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnGrab);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnDownloadEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnDownload);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnUpgradeEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnUpgrade);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnImportCompleteEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnImportComplete);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnRenameEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnRename);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnSeriesAddEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnSeriesAdd);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnSeriesDeleteEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnSeriesDelete);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnEpisodeFileDeleteEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnEpisodeFileDelete);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnEpisodeFileDeleteForUpgradeEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnEpisodeFileDeleteForUpgrade);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnHealthIssueEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnHealthIssue);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnHealthRestoredEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnHealthRestored);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnApplicationUpdateEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnApplicationUpdate);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        public async Task<List<INotification>> OnManualInteractionEnabled(bool filterBlockedNotifications = true)
        {
            var enabled = (await GetAvailableProviders()).Where(n => ((NotificationDefinition)n.Definition).OnManualInteractionRequired);

            if (filterBlockedNotifications)
            {
                return await FilterBlockedNotifications(enabled);
            }

            return enabled.ToList();
        }

        private async Task<List<INotification>> FilterBlockedNotifications(IEnumerable<INotification> notifications)
        {
            var blockedNotifications = (await _notificationStatusService.GetBlockedProviders()).ToDictionary(v => v.ProviderId, v => v);
            var result = new List<INotification>();

            foreach (var notification in notifications)
            {
                if (blockedNotifications.TryGetValue(notification.Definition.Id, out var notificationStatus))
                {
                    _logger.Debug("Temporarily ignoring notification {0} till {1} due to recent failures.", notification.Definition.Name, notificationStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                result.Add(notification);
            }

            return result;
        }

        public override void SetProviderCharacteristics(INotification provider, NotificationDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.SupportsOnGrab = provider.SupportsOnGrab;
            definition.SupportsOnDownload = provider.SupportsOnDownload;
            definition.SupportsOnUpgrade = provider.SupportsOnUpgrade;
            definition.SupportsOnImportComplete = provider.SupportsOnImportComplete;
            definition.SupportsOnRename = provider.SupportsOnRename;
            definition.SupportsOnSeriesAdd = provider.SupportsOnSeriesAdd;
            definition.SupportsOnSeriesDelete = provider.SupportsOnSeriesDelete;
            definition.SupportsOnEpisodeFileDelete = provider.SupportsOnEpisodeFileDelete;
            definition.SupportsOnEpisodeFileDeleteForUpgrade = provider.SupportsOnEpisodeFileDeleteForUpgrade;
            definition.SupportsOnHealthIssue = provider.SupportsOnHealthIssue;
            definition.SupportsOnHealthRestored = provider.SupportsOnHealthRestored;
            definition.SupportsOnApplicationUpdate = provider.SupportsOnApplicationUpdate;
            definition.SupportsOnManualInteractionRequired = provider.SupportsOnManualInteractionRequired;
        }

        public override async Task<ValidationResult> Test(NotificationDefinition definition)
        {
            var result = await base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                await _notificationStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                await _notificationStatusService.RecordFailure(definition.Id);
            }

            return result;
        }
    }
}
