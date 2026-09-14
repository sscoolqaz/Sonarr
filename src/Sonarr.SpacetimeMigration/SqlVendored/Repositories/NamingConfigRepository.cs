using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Organizer
{
    public class NamingConfigRepository : BasicRepository<NamingConfig>, INamingConfigRepository
    {
        public NamingConfigRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
