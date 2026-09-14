using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Housekeeping;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real CleanupDuplicateMetadataFiles runs three raw SQL DELETEs (MIN(Id) ... GROUP BY ...
    // HAVING COUNT() > 1) against IMainDatabase directly - a no-op once SpacetimeDB is the write
    // path, same reasoning as SpacetimeCleanupOrphanedEpisodes for why this is additional, not a
    // replacement.
    public class SpacetimeCleanupDuplicateMetadataFiles : IHousekeepingTask
    {
        private readonly IMetadataFileRepository _metadataFileRepository;

        public SpacetimeCleanupDuplicateMetadataFiles(IMetadataFileRepository metadataFileRepository)
        {
            _metadataFileRepository = metadataFileRepository;
        }

        public async Task Clean()
        {
            var allFiles = (await _metadataFileRepository.All()).ToList();

            await DeleteDuplicates(allFiles.Where(f => f.Type == MetadataType.SeriesMetadata), f => (f.SeriesId, f.Consumer));
            await DeleteDuplicates(allFiles.Where(f => f.Type == MetadataType.EpisodeMetadata && f.EpisodeFileId != null), f => (f.EpisodeFileId, f.Consumer));
            await DeleteDuplicates(allFiles.Where(f => f.Type == MetadataType.EpisodeImage && f.EpisodeFileId != null), f => (f.EpisodeFileId, f.Consumer));
        }

        // The real SQL deletes the MIN(Id) row of each duplicate group (keeping the
        // newest/highest-id one), for any group with more than one row. The real query's
        // HAVING COUNT("EpisodeFileId") > 1 counts a column, which excludes NULLs - so rows with
        // no EpisodeFileId are never flagged as duplicates there; the EpisodeFileId != null
        // filter above reproduces that exclusion (SeriesId is NOT NULL for SeriesMetadata rows,
        // so no equivalent filter is needed on that grouping).
        private async Task DeleteDuplicates<TKey>(System.Collections.Generic.IEnumerable<MetadataFile> files, System.Func<MetadataFile, TKey> groupKey)
        {
            var duplicateIds = files
                .GroupBy(groupKey)
                .Where(g => g.Count() > 1)
                .Select(g => g.Min(f => f.Id));

            foreach (var id in duplicateIds)
            {
                await _metadataFileRepository.Delete(id);
            }
        }
    }
}
