using System.Linq;
using System.Threading.Tasks;
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

        public async Task Clean()
        {
            var validProviderIds = (await _importListRepository.All()).Select(i => i.Id).ToHashSet();
            await SpacetimeOrphanCleanup.DeleteWhereParentMissing(await _statusRepository.All(), validProviderIds, s => s.ProviderId, _statusRepository.Delete);
        }
    }
}
