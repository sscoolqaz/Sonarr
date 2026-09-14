using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Download;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Tags
{
    public interface ITagService
    {
        Task<Tag> GetTag(int tagId);
        Task<Tag> GetTag(string tag);
        Task<List<Tag>> GetTags(IEnumerable<int> ids);
        Task<TagDetails> Details(int tagId);
        Task<List<TagDetails>> Details();
        Task<List<Tag>> All();
        Task<Tag> Add(Tag tag);
        Task<Tag> Update(Tag tag);
        Task Delete(int tagId);
    }

    public class TagService : ITagService
    {
        private readonly ITagRepository _repo;
        private readonly IEventAggregator _eventAggregator;
        private readonly IDelayProfileService _delayProfileService;
        private readonly IImportListFactory _importListFactory;
        private readonly INotificationFactory _notificationFactory;
        private readonly IReleaseProfileService _releaseProfileService;
        private readonly ISeriesService _seriesService;
        private readonly IIndexerFactory _indexerService;
        private readonly IAutoTaggingService _autoTaggingService;
        private readonly IDownloadClientFactory _downloadClientFactory;

        public TagService(ITagRepository repo,
                          IEventAggregator eventAggregator,
                          IDelayProfileService delayProfileService,
                          IImportListFactory importListFactory,
                          INotificationFactory notificationFactory,
                          IReleaseProfileService releaseProfileService,
                          ISeriesService seriesService,
                          IIndexerFactory indexerService,
                          IAutoTaggingService autoTaggingService,
                          IDownloadClientFactory downloadClientFactory)
        {
            _repo = repo;
            _eventAggregator = eventAggregator;
            _delayProfileService = delayProfileService;
            _importListFactory = importListFactory;
            _notificationFactory = notificationFactory;
            _releaseProfileService = releaseProfileService;
            _seriesService = seriesService;
            _indexerService = indexerService;
            _autoTaggingService = autoTaggingService;
            _downloadClientFactory = downloadClientFactory;
        }

        public async Task<Tag> GetTag(int tagId)
        {
            return await _repo.Get(tagId);
        }

        public async Task<Tag> GetTag(string tag)
        {
            if (tag.All(char.IsDigit))
            {
                return await _repo.Get(int.Parse(tag));
            }
            else
            {
                return await _repo.GetByLabel(tag);
            }
        }

        public async Task<List<Tag>> GetTags(IEnumerable<int> ids)
        {
            return (await _repo.Get(ids)).ToList();
        }

        public async Task<TagDetails> Details(int tagId)
        {
            var tag = await GetTag(tagId);
            var delayProfiles = await _delayProfileService.AllForTag(tagId);
            var importLists = await _importListFactory.AllForTag(tagId);
            var notifications = await _notificationFactory.AllForTag(tagId);
            var releaseProfiles = await _releaseProfileService.AllForTag(tagId);
            var excludedReleaseProfiles = await _releaseProfileService.AllExcludedForTag(tagId);
            var series = await _seriesService.AllForTag(tagId);
            var indexers = await _indexerService.AllForTag(tagId);
            var autoTags = await _autoTaggingService.AllForTag(tagId);
            var downloadClients = await _downloadClientFactory.AllForTag(tagId);

            return new TagDetails
            {
                Id = tagId,
                Label = tag.Label,
                DelayProfileIds = delayProfiles.Select(c => c.Id).ToList(),
                ImportListIds = importLists.Select(c => c.Id).ToList(),
                NotificationIds = notifications.Select(c => c.Id).ToList(),
                RestrictionIds = releaseProfiles.Select(c => c.Id).ToList(),
                ExcludedReleaseProfileIds = excludedReleaseProfiles.Select(c => c.Id).ToList(),
                SeriesIds = series.Select(c => c.Id).ToList(),
                IndexerIds = indexers.Select(c => c.Id).ToList(),
                AutoTagIds = autoTags.Select(c => c.Id).ToList(),
                DownloadClientIds = downloadClients.Select(c => c.Id).ToList()
            };
        }

        public async Task<List<TagDetails>> Details()
        {
            var tags = await All();
            var delayProfiles = await _delayProfileService.All();
            var importLists = await _importListFactory.All();
            var notifications = await _notificationFactory.All();
            var releaseProfiles = await _releaseProfileService.All();
            var excludedReleaseProfiles = await _releaseProfileService.All();
            var series = await _seriesService.GetAllSeriesTags();
            var indexers = await _indexerService.All();
            var autoTags = await _autoTaggingService.All();
            var downloadClients = await _downloadClientFactory.All();

            var details = new List<TagDetails>();

            foreach (var tag in tags)
            {
                details.Add(new TagDetails
                    {
                        Id = tag.Id,
                        Label = tag.Label,
                        DelayProfileIds = delayProfiles.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                        ImportListIds = importLists.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                        NotificationIds = notifications.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                        RestrictionIds = releaseProfiles.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                        ExcludedReleaseProfileIds = excludedReleaseProfiles.Where(c => c.ExcludedTags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                        SeriesIds = series.Where(c => c.Value.Contains(tag.Id)).Select(c => c.Key).ToList(),
                        IndexerIds = indexers.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                        AutoTagIds = GetAutoTagIds(tag, autoTags),
                        DownloadClientIds = downloadClients.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList(),
                    });
            }

            return details;
        }

        public async Task<List<Tag>> All()
        {
            return (await _repo.All()).OrderBy(t => t.Label).ToList();
        }

        public async Task<Tag> Add(Tag tag)
        {
            var existingTag = await _repo.FindByLabel(tag.Label);

            if (existingTag != null)
            {
                return existingTag;
            }

            tag.Label = tag.Label.ToLowerInvariant();

            await _repo.Insert(tag);
            _eventAggregator.PublishEvent(new TagsUpdatedEvent());

            return tag;
        }

        public async Task<Tag> Update(Tag tag)
        {
            tag.Label = tag.Label.ToLowerInvariant();

            await _repo.Update(tag);
            _eventAggregator.PublishEvent(new TagsUpdatedEvent());

            return tag;
        }

        public async Task Delete(int tagId)
        {
            var details = await Details(tagId);
            if (details.InUse)
            {
                throw new ModelConflictException(typeof(Tag), tagId, $"'{details.Label}' cannot be deleted since it's still in use");
            }

            await _repo.Delete(tagId);
            _eventAggregator.PublishEvent(new TagsUpdatedEvent());
        }

        private List<int> GetAutoTagIds(Tag tag, List<AutoTag> autoTags)
        {
            var autoTagIds = autoTags.Where(c => c.Tags.Contains(tag.Id)).Select(c => c.Id).ToList();

            foreach (var autoTag in autoTags)
            {
                foreach (var specification in autoTag.Specifications)
                {
                    if (specification is TagSpecification tagSpecification && tagSpecification.Value == tag.Id)
                    {
                        autoTagIds.Add(autoTag.Id);
                    }
                }
            }

            return autoTagIds.Distinct().ToList();
        }
    }
}
