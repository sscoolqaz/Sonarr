using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public class ImportListExclusionRepository : BasicRepository<ImportListExclusion>, IImportListExclusionRepository
    {
        public ImportListExclusionRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<ImportListExclusion> FindByTvdbId(int tvdbId)
        {
            return Task.FromResult(Query(m => m.TvdbId == tvdbId).SingleOrDefault());
        }
    }
}
