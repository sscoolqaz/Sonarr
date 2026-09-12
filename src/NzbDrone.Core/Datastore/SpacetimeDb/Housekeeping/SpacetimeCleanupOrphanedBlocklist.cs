using System.Linq;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedBlocklist : IHousekeepingTask
    {
        private readonly IBlocklistRepository _blocklistRepository;
        private readonly ISeriesRepository _seriesRepository;

        public SpacetimeCleanupOrphanedBlocklist(IBlocklistRepository blocklistRepository, ISeriesRepository seriesRepository)
        {
            _blocklistRepository = blocklistRepository;
            _seriesRepository = seriesRepository;
        }

        public void Clean()
        {
            var validSeriesIds = _seriesRepository.All().Select(s => s.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_blocklistRepository.All(), validSeriesIds, b => b.SeriesId, _blocklistRepository.Delete);
        }
    }
}
