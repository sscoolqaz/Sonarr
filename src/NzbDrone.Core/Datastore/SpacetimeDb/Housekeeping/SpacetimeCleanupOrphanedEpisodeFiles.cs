using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // See SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    // Reverse direction from every other cleanup task here: the real query is
    // EpisodeFiles LEFT JOIN Episodes ON EpisodeFiles.Id = Episodes.EpisodeFileId
    // WHERE Episodes.Id IS NULL - an EpisodeFile is the orphan here, "parented" by whichever
    // Episode (if any) currently points back at it via EpisodeFileId, not the usual
    // child-has-a-foreign-key-to-its-parent shape.
    public class SpacetimeCleanupOrphanedEpisodeFiles : IHousekeepingTask
    {
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IEpisodeRepository _episodeRepository;

        public SpacetimeCleanupOrphanedEpisodeFiles(IMediaFileRepository mediaFileRepository, IEpisodeRepository episodeRepository)
        {
            _mediaFileRepository = mediaFileRepository;
            _episodeRepository = episodeRepository;
        }

        public async Task Clean()
        {
            var referencedFileIds = (await _episodeRepository.All())
                .Where(e => e.EpisodeFileId > 0)
                .Select(e => e.EpisodeFileId)
                .ToHashSet();

            foreach (var file in (await _mediaFileRepository.All()).Where(f => !referencedFileIds.Contains(f.Id)).ToList())
            {
                await _mediaFileRepository.Delete(file.Id);
            }
        }
    }
}
