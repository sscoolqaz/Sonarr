using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public class IndexerRepository : ProviderRepository<IndexerDefinition>, IIndexerRepository
    {
        public IndexerRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<IndexerDefinition> FindByName(string name)
        {
            return Task.FromResult(Query(i => i.Name == name).SingleOrDefault());
        }
    }
}
