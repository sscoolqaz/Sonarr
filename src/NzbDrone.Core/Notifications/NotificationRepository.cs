using System.Threading.Tasks;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    public interface INotificationRepository : IProviderRepository<NotificationDefinition>
    {
        Task UpdateSettings(NotificationDefinition model);
    }
}
