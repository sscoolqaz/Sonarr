using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.Delay
{
    public class DelayProfileRepository : BasicRepository<DelayProfile>, IDelayProfileRepository
    {
        public DelayProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
