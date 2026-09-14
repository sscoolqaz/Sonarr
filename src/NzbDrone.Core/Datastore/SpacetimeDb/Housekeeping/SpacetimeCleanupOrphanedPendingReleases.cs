using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedPendingReleases : IHousekeepingTask
    {
        private readonly IPendingReleaseRepository _pendingReleaseRepository;
        private readonly ISeriesRepository _seriesRepository;

        public SpacetimeCleanupOrphanedPendingReleases(IPendingReleaseRepository pendingReleaseRepository, ISeriesRepository seriesRepository)
        {
            _pendingReleaseRepository = pendingReleaseRepository;
            _seriesRepository = seriesRepository;
        }

        public async Task Clean()
        {
            var validSeriesIds = (await _seriesRepository.All()).Select(s => s.Id).ToHashSet();
            await SpacetimeOrphanCleanup.DeleteWhereParentMissing(await _pendingReleaseRepository.All(), validSeriesIds, p => p.SeriesId, _pendingReleaseRepository.Delete);
        }
    }
}
