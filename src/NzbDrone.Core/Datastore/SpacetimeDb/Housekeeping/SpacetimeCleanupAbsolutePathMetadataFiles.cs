using System.Linq;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Housekeeping;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real CleanupAbsolutePathMetadataFiles runs raw SQL LIKE patterns against IMainDatabase
    // directly - a no-op once SpacetimeDB is the write path, same reasoning as
    // SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement.
    public class SpacetimeCleanupAbsolutePathMetadataFiles : IHousekeepingTask
    {
        private readonly IMetadataFileRepository _metadataFileRepository;

        public SpacetimeCleanupAbsolutePathMetadataFiles(IMetadataFileRepository metadataFileRepository)
        {
            _metadataFileRepository = metadataFileRepository;
        }

        public void Clean()
        {
            foreach (var file in _metadataFileRepository.All().Where(f => IsAbsolutePath(f.RelativePath)).ToList())
            {
                _metadataFileRepository.Delete(file.Id);
            }
        }

        // Mirrors the real SQL's three LIKE patterns exactly: '_:\%' (drive-letter-rooted,
        // e.g. "C:\..."), '\%' (backslash/UNC-rooted), '/%' (unix-rooted).
        private static bool IsAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (path.Length >= 3 && path[1] == ':' && path[2] == '\\')
            {
                return true;
            }

            return path.StartsWith('\\') || path.StartsWith('/');
        }
    }
}
