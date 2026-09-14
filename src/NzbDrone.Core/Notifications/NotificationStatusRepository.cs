using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Notifications
{
    public interface INotificationStatusRepository : IProviderStatusRepository<NotificationStatus>
    {
    }
}
