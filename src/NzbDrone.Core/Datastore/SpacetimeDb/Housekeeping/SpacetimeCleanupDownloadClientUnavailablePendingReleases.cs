using System;
using System.Linq;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Housekeeping;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real CleanupDownloadClientUnavailablePendingReleases runs a raw SQL DELETE against
    // IMainDatabase directly - a no-op once SpacetimeDB is the write path, same reasoning as
    // SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupDownloadClientUnavailablePendingReleases : IHousekeepingTask
    {
        private readonly IPendingReleaseRepository _pendingReleaseRepository;

        public SpacetimeCleanupDownloadClientUnavailablePendingReleases(IPendingReleaseRepository pendingReleaseRepository)
        {
            _pendingReleaseRepository = pendingReleaseRepository;
        }

        public void Clean()
        {
            var twoWeeksAgo = DateTime.UtcNow.AddDays(-14);

            var stale = _pendingReleaseRepository.All()
                .Where(p => p.Added < twoWeeksAgo &&
                            (p.Reason == PendingReleaseReason.DownloadClientUnavailable || p.Reason == PendingReleaseReason.Fallback))
                .ToList();

            foreach (var release in stale)
            {
                _pendingReleaseRepository.Delete(release.Id);
            }
        }
    }
}
