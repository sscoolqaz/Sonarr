using System.Linq;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Indexers;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedIndexerStatus : IHousekeepingTask
    {
        private readonly IIndexerStatusRepository _statusRepository;
        private readonly IIndexerRepository _indexerRepository;

        public SpacetimeCleanupOrphanedIndexerStatus(IIndexerStatusRepository statusRepository, IIndexerRepository indexerRepository)
        {
            _statusRepository = statusRepository;
            _indexerRepository = indexerRepository;
        }

        public void Clean()
        {
            var validProviderIds = _indexerRepository.All().Select(i => i.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_statusRepository.All(), validProviderIds, s => s.ProviderId, _statusRepository.Delete);
        }
    }
}
