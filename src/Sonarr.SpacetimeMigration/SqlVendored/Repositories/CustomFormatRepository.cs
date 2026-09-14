using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.CustomFormats
{
    public class CustomFormatRepository : BasicRepository<CustomFormat>, ICustomFormatRepository
    {
        public CustomFormatRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }
}
