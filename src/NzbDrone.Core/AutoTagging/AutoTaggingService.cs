using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.AutoTagging
{
    public interface IAutoTaggingService
    {
        Task Update(AutoTag autoTag);
        Task<AutoTag> Insert(AutoTag autoTag);
        Task<List<AutoTag>> All();
        Task<AutoTag> GetById(int id);
        Task Delete(int id);
        Task<List<AutoTag>> AllForTag(int tagId);
        Task<AutoTaggingChanges> GetTagChanges(Series series);
    }

    public class AutoTaggingService : IAutoTaggingService
    {
        private readonly IAutoTaggingRepository _repository;
        private readonly RootFolderService _rootFolderService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICached<Dictionary<int, AutoTag>> _cache;

        public AutoTaggingService(IAutoTaggingRepository repository,
                                  RootFolderService rootFolderService,
                                  IEventAggregator eventAggregator,
                                  ICacheManager cacheManager)
        {
            _repository = repository;
            _rootFolderService = rootFolderService;
            _eventAggregator = eventAggregator;

            _cache = cacheManager.GetCache<Dictionary<int, AutoTag>>(typeof(AutoTag), "autoTags");
        }

        private async Task<Dictionary<int, AutoTag>> AllDictionary()
        {
            var cached = _cache.Find("all");

            if (cached != null)
            {
                return cached;
            }

            var all = (await _repository.All()).ToDictionary(m => m.Id);
            _cache.Set("all", all);

            return all;
        }

        public async Task<List<AutoTag>> All()
        {
            return (await AllDictionary()).Values.ToList();
        }

        public async Task<AutoTag> GetById(int id)
        {
            return (await AllDictionary())[id];
        }

        public async Task Update(AutoTag autoTag)
        {
            await _repository.Update(autoTag);

            _cache.Clear();
            _eventAggregator.PublishEvent(new AutoTagsUpdatedEvent());
        }

        public async Task<AutoTag> Insert(AutoTag autoTag)
        {
            var result = await _repository.Insert(autoTag);

            _cache.Clear();
            _eventAggregator.PublishEvent(new AutoTagsUpdatedEvent());

            return result;
        }

        public async Task Delete(int id)
        {
            await _repository.Delete(id);

            _cache.Clear();
            _eventAggregator.PublishEvent(new AutoTagsUpdatedEvent());
        }

        public async Task<List<AutoTag>> AllForTag(int tagId)
        {
            return (await All()).Where(p => p.Tags.Contains(tagId))
                .ToList();
        }

        public async Task<AutoTaggingChanges> GetTagChanges(Series series)
        {
            var autoTags = await All();
            var changes = new AutoTaggingChanges();

            if (autoTags.Empty())
            {
                return changes;
            }

            // Set the root folder path on the series
            series.RootFolderPath = await _rootFolderService.GetBestRootFolderPath(series.Path);

            foreach (var autoTag in autoTags)
            {
                var specificationMatches = autoTag.Specifications
                    .GroupBy(t => t.GetType())
                    .Select(g => new SpecificationMatchesGroup
                    {
                        Matches = g.ToDictionary(t => t, t => t.IsSatisfiedBy(series))
                    })
                    .ToList();

                var allMatch = specificationMatches.All(x => x.DidMatch);
                var tags = autoTag.Tags;

                if (allMatch)
                {
                    foreach (var tag in tags)
                    {
                        if (!series.Tags.Contains(tag))
                        {
                            changes.TagsToAdd.Add(tag);
                        }
                    }

                    continue;
                }

                if (autoTag.RemoveTagsAutomatically)
                {
                    foreach (var tag in tags)
                    {
                        changes.TagsToRemove.Add(tag);
                    }
                }
            }

            return changes;
        }
    }
}
