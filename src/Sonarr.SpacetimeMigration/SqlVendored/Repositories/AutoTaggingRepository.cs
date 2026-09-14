using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.AutoTagging
{
    public class AutoTaggingRepository : BasicRepository<AutoTag>, IAutoTaggingRepository
    {
        public AutoTaggingRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
