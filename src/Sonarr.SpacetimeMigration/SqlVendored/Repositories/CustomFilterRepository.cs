using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.CustomFilters
{
    public class CustomFilterRepository : BasicRepository<CustomFilter>, ICustomFilterRepository
    {
        public CustomFilterRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
