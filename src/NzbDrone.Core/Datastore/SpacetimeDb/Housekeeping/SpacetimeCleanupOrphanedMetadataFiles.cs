using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupOrphanedMetadataFiles : IHousekeepingTask
    {
        private readonly IMetadataFileRepository _metadataFileRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly IMediaFileRepository _mediaFileRepository;

        public SpacetimeCleanupOrphanedMetadataFiles(
            IMetadataFileRepository metadataFileRepository,
            ISeriesRepository seriesRepository,
            IMediaFileRepository mediaFileRepository)
        {
            _metadataFileRepository = metadataFileRepository;
            _seriesRepository = seriesRepository;
            _mediaFileRepository = mediaFileRepository;
        }

        public async Task Clean()
        {
            var validSeriesIds = (await _seriesRepository.All()).Select(s => s.Id).ToHashSet();
            await SpacetimeOrphanCleanup.DeleteWhereParentMissing(await _metadataFileRepository.All(), validSeriesIds, f => f.SeriesId, _metadataFileRepository.Delete);

            var validFileIds = (await _mediaFileRepository.All()).Select(f => f.Id).ToHashSet();

            foreach (var metadataFile in (await _metadataFileRepository.All()).ToList())
            {
                var hasFile = metadataFile.EpisodeFileId is > 0;

                if (hasFile && !validFileIds.Contains(metadataFile.EpisodeFileId.Value))
                {
                    await _metadataFileRepository.Delete(metadataFile.Id);
                }
                else if (!hasFile && (metadataFile.Type == MetadataType.EpisodeMetadata || metadataFile.Type == MetadataType.EpisodeImage))
                {
                    await _metadataFileRepository.Delete(metadataFile.Id);
                }
            }
        }
    }
}
