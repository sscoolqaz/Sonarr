using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Notifications
{
    public class NotificationRepository : ProviderRepository<NotificationDefinition>, INotificationRepository
    {
        public NotificationRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public void UpdateSettings(NotificationDefinition model)
        {
            SetFields(model, m => m.Settings);
        }
    }
}
