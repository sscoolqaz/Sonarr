using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Additional IHousekeepingTask, not a replacement for the real CleanupOrphanedEpisodes - that
    // one's raw SQL against IMainDatabase is a harmless no-op once SpacetimeDB is the write path
    // (there's nothing left in the real Episodes table for it to find), and DryIoc's
    // IEnumerable<IHousekeepingTask> collection registration (via AutoAddServices' assembly scan)
    // has no clean way to swap out one specific auto-registered implementation, so this is
    // registered alongside it instead.
    public class SpacetimeCleanupOrphanedEpisodes : IHousekeepingTask
    {
        private readonly IEpisodeRepository _episodeRepository;
        private readonly ISeriesRepository _seriesRepository;

        public SpacetimeCleanupOrphanedEpisodes(IEpisodeRepository episodeRepository, ISeriesRepository seriesRepository)
        {
            _episodeRepository = episodeRepository;
            _seriesRepository = seriesRepository;
        }

        public async Task Clean()
        {
            var validSeriesIds = (await _seriesRepository.All()).Select(s => s.Id).ToHashSet();
            await SpacetimeOrphanCleanup.DeleteWhereParentMissing(await _episodeRepository.All(), validSeriesIds, e => e.SeriesId, _episodeRepository.Delete);
        }
    }
}
