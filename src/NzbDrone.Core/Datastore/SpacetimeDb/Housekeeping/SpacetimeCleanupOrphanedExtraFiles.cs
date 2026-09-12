using System.Linq;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real CleanupOrphanedExtraFiles targets the "ExtraFiles" SQL table, which is OtherExtraFile's
    // table (TableMapping.cs: Mapper.Entity<OtherExtraFile>("ExtraFiles")) - SubtitleFile and
    // MetadataFile each have their own distinct tables/cleanup tasks. See
    // SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedExtraFiles : IHousekeepingTask
    {
        private readonly IOtherExtraFileRepository _extraFileRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IMediaFileRepository _mediaFileRepository;

        public SpacetimeCleanupOrphanedExtraFiles(
            IOtherExtraFileRepository extraFileRepository,
            ISeriesRepository seriesRepository,
            IMediaFileRepository mediaFileRepository)
        {
            _extraFileRepository = extraFileRepository;
            _seriesRepository = seriesRepository;
            _mediaFileRepository = mediaFileRepository;
        }

        public void Clean()
        {
            var validSeriesIds = _seriesRepository.All().Select(s => s.Id).ToHashSet();
            SpacetimeOrphanCleanup.DeleteWhereParentMissing(_extraFileRepository.All(), validSeriesIds, f => f.SeriesId, _extraFileRepository.Delete);

            var validFileIds = _mediaFileRepository.All().Select(f => f.Id).ToHashSet();

            foreach (var extraFile in _extraFileRepository.All().ToList())
            {
                var hasFile = extraFile.EpisodeFileId is > 0;

                if (hasFile && !validFileIds.Contains(extraFile.EpisodeFileId.Value))
                {
                    _extraFileRepository.Delete(extraFile.Id);
                }
                else if (!hasFile)
                {
                    _extraFileRepository.Delete(extraFile.Id);
                }
            }
        }
    }
}
