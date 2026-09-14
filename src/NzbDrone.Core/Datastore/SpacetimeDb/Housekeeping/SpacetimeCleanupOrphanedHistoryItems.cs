using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.History;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedHistoryItems : IHousekeepingTask
    {
        private readonly IHistoryRepository _historyRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IEpisodeRepository _episodeRepository;

        public SpacetimeCleanupOrphanedHistoryItems(IHistoryRepository historyRepository, ISeriesRepository seriesRepository, IEpisodeRepository episodeRepository)
        {
            _historyRepository = historyRepository;
            _seriesRepository = seriesRepository;
            _episodeRepository = episodeRepository;
        }

        public async Task Clean()
        {
            var validSeriesIds = (await _seriesRepository.All()).Select(s => s.Id).ToHashSet();
            var validEpisodeIds = (await _episodeRepository.All()).Select(e => e.Id).ToHashSet();

            var all = (await _historyRepository.All()).ToList();

            await SpacetimeOrphanCleanup.DeleteWhereParentMissing(all, validSeriesIds, h => h.SeriesId, _historyRepository.Delete);

            // Re-fetch: a row already deleted by the SeriesId pass above must not be looked up
            // again (Delete on an already-deleted id would throw), same reason the real
            // CleanupOrphanedBySeries/CleanupOrphanedByEpisode run as two separate DELETE
            // statements rather than one combined predicate.
            var remaining = await _historyRepository.All();
            await SpacetimeOrphanCleanup.DeleteWhereParentMissing(remaining, validEpisodeIds, h => h.EpisodeId, _historyRepository.Delete);
        }
    }
}
