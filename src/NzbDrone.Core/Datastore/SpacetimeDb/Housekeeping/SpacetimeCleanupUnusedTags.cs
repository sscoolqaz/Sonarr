using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real CleanupUnusedTags queries the Tags JSON column directly via raw SQL across Series,
    // Notifications, DelayProfiles, ReleaseProfiles, ImportLists, Indexers and DownloadClients,
    // plus AutoTaggingRepository's TagSpecifications - a no-op once SpacetimeDB is the write path
    // for any of those tables, same reasoning as SpacetimeCleanupOrphanedEpisodes for why this is
    // additional, not a replacement. All 7 tag-bearing entities plus AutoTagging are already
    // Spacetime-aware via this port's existing DI pinning, so this is reimplemented entirely
    // against their normal swappable repository interfaces instead of raw SQL.
    public class SpacetimeCleanupUnusedTags : IHousekeepingTask
    {
        private readonly ITagRepository _tagRepository;
        private readonly ISeriesRepository _seriesRepository;
        private readonly INotificationRepository _notificationRepository;
        private readonly IDelayProfileRepository _delayProfileRepository;
        private readonly IRestrictionRepository _releaseProfileRepository;
        private readonly IImportListRepository _importListRepository;
        private readonly IIndexerRepository _indexerRepository;
        private readonly IDownloadClientRepository _downloadClientRepository;
        private readonly IAutoTaggingRepository _autoTaggingRepository;

        public SpacetimeCleanupUnusedTags(
            ITagRepository tagRepository,
            ISeriesRepository seriesRepository,
            INotificationRepository notificationRepository,
            IDelayProfileRepository delayProfileRepository,
            IRestrictionRepository releaseProfileRepository,
            IImportListRepository importListRepository,
            IIndexerRepository indexerRepository,
            IDownloadClientRepository downloadClientRepository,
            IAutoTaggingRepository autoTaggingRepository)
        {
            _tagRepository = tagRepository;
            _seriesRepository = seriesRepository;
            _notificationRepository = notificationRepository;
            _delayProfileRepository = delayProfileRepository;
            _releaseProfileRepository = releaseProfileRepository;
            _importListRepository = importListRepository;
            _indexerRepository = indexerRepository;
            _downloadClientRepository = downloadClientRepository;
            _autoTaggingRepository = autoTaggingRepository;
        }

        public async Task Clean()
        {
            var usedTags = new HashSet<int>();

            usedTags.UnionWith((await _seriesRepository.All()).SelectMany(s => s.Tags));
            usedTags.UnionWith((await _notificationRepository.All()).SelectMany(n => n.Tags));
            usedTags.UnionWith((await _delayProfileRepository.All()).SelectMany(d => d.Tags));
            usedTags.UnionWith((await _releaseProfileRepository.All()).SelectMany(r => r.Tags.Concat(r.ExcludedTags)));
            usedTags.UnionWith((await _importListRepository.All()).SelectMany(i => i.Tags));
            usedTags.UnionWith((await _indexerRepository.All()).SelectMany(i => i.Tags));
            usedTags.UnionWith((await _downloadClientRepository.All()).SelectMany(d => d.Tags));

            var autoTags = (await _autoTaggingRepository.All()).ToList();

            // Real task scans AutoTagging's own Tags column (the tags an auto-tag applies to a
            // series) as one of its 8 generic tag-bearing tables, separately from the
            // TagSpecification scan below (the tags an auto-tag's specifications reference).
            usedTags.UnionWith(autoTags.SelectMany(t => t.Tags));

            usedTags.UnionWith(autoTags
                .SelectMany(t => t.Specifications)
                .OfType<TagSpecification>()
                .Select(s => s.Value));

            var unused = (await _tagRepository.All()).Where(t => !usedTags.Contains(t.Id)).ToList();

            foreach (var tag in unused)
            {
                await _tagRepository.Delete(tag.Id);
            }
        }
    }
}
