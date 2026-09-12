using System.Linq;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.ImportLists;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedImportListStatus : IHousekeepingTask
    {
        private readonly IImportListStatusRepository _statusRepository;
        private readonly IImportListRepository _importListRepository;

        public SpacetimeCleanupOrphanedImportListStatus(IImportListStatusRepository statusRepository, IImportListRepository importListRepository)
        {
            _statusRepository = statusRepository;
            _importListRepository = importListRepository;
        }

        public void Clean()
        {
            var validProviderIds = _importListRepository.All().Select(i => i.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_statusRepository.All(), validProviderIds, s => s.ProviderId, _statusRepository.Delete);
        }
    }
}
