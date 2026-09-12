using System.Linq;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Notifications;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedNotificationStatus : IHousekeepingTask
    {
        private readonly INotificationStatusRepository _statusRepository;
        private readonly INotificationRepository _notificationRepository;

        public SpacetimeCleanupOrphanedNotificationStatus(INotificationStatusRepository statusRepository, INotificationRepository notificationRepository)
        {
            _statusRepository = statusRepository;
            _notificationRepository = notificationRepository;
        }

        public void Clean()
        {
            var validProviderIds = _notificationRepository.All().Select(n => n.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_statusRepository.All(), validProviderIds, s => s.ProviderId, _statusRepository.Delete);
        }
    }
}
