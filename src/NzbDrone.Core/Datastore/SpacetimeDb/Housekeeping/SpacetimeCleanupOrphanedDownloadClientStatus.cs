using System.Linq;
using NzbDrone.Core.Download;
using NzbDrone.Core.Housekeeping;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedDownloadClientStatus : IHousekeepingTask
    {
        private readonly IDownloadClientStatusRepository _statusRepository;
        private readonly IDownloadClientRepository _downloadClientRepository;

        public SpacetimeCleanupOrphanedDownloadClientStatus(IDownloadClientStatusRepository statusRepository, IDownloadClientRepository downloadClientRepository)
        {
            _statusRepository = statusRepository;
            _downloadClientRepository = downloadClientRepository;
        }

        public void Clean()
        {
            var validProviderIds = _downloadClientRepository.All().Select(d => d.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_statusRepository.All(), validProviderIds, s => s.ProviderId, _statusRepository.Delete);
        }
    }
}
