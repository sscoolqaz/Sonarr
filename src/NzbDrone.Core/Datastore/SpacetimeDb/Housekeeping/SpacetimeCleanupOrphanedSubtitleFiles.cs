using System.Linq;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedSubtitleFiles : IHousekeepingTask
    {
        private readonly ISubtitleFileRepository _subtitleFileRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IMediaFileRepository _mediaFileRepository;

        public SpacetimeCleanupOrphanedSubtitleFiles(
            ISubtitleFileRepository subtitleFileRepository,
            ISeriesRepository seriesRepository,
            IMediaFileRepository mediaFileRepository)
        {
            _subtitleFileRepository = subtitleFileRepository;
            _seriesRepository = seriesRepository;
            _mediaFileRepository = mediaFileRepository;
        }

        public void Clean()
        {
            var validSeriesIds = _seriesRepository.All().Select(s => s.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_subtitleFileRepository.All(), validSeriesIds, f => f.SeriesId, _subtitleFileRepository.Delete);

            var validFileIds = _mediaFileRepository.All().Select(f => f.Id).ToHashSet();

            foreach (var subtitleFile in _subtitleFileRepository.All().ToList())
            {
                var hasFile = subtitleFile.EpisodeFileId is > 0;

                if (hasFile && !validFileIds.Contains(subtitleFile.EpisodeFileId.Value))
                {
                    _subtitleFileRepository.Delete(subtitleFile.Id);
                }
                else if (!hasFile)
                {
                    _subtitleFileRepository.Delete(subtitleFile.Id);
                }
            }
        }
    }
}
