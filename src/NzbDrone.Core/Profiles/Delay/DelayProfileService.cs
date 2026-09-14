using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Profiles.Delay
{
    public interface IDelayProfileService
    {
        Task<DelayProfile> Add(DelayProfile profile);
        Task<DelayProfile> Update(DelayProfile profile);
        Task Delete(int id);
        Task<List<DelayProfile>> All();
        Task<DelayProfile> Get(int id);
        Task<List<DelayProfile>> AllForTag(int tagId);
        Task<List<DelayProfile>> AllForTags(HashSet<int> tagIds);
        Task<DelayProfile> BestForTags(HashSet<int> tagIds);
        Task<List<DelayProfile>> Reorder(int id, int? afterId);
    }

    public class DelayProfileService : IDelayProfileService
    {
        private readonly IDelayProfileRepository _repo;
        private readonly ICached<DelayProfile> _bestForTagsCache;

        public DelayProfileService(IDelayProfileRepository repo, ICacheManager cacheManager)
        {
            _repo = repo;
            _bestForTagsCache = cacheManager.GetCache<DelayProfile>(GetType(), "best");
        }

        public async Task<DelayProfile> Add(DelayProfile profile)
        {
            profile.Order = await _repo.Count();

            var result = await _repo.Insert(profile);
            _bestForTagsCache.Clear();

            return result;
        }

        public async Task<DelayProfile> Update(DelayProfile profile)
        {
            var result = await _repo.Update(profile);
            _bestForTagsCache.Clear();
            return result;
        }

        public async Task Delete(int id)
        {
            await _repo.Delete(id);

            var all = (await All()).OrderBy(d => d.Order).ToList();

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Id == 1)
                {
                    continue;
                }

                all[i].Order = i + 1;
            }

            await _repo.UpdateMany(all);
            _bestForTagsCache.Clear();
        }

        public async Task<List<DelayProfile>> All()
        {
            return (await _repo.All()).ToList();
        }

        public async Task<DelayProfile> Get(int id)
        {
            return await _repo.Get(id);
        }

        public async Task<List<DelayProfile>> AllForTag(int tagId)
        {
            return (await All()).Where(r => r.Tags.Contains(tagId))
                        .ToList();
        }

        public async Task<List<DelayProfile>> AllForTags(HashSet<int> tagIds)
        {
            return (await All()).Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty()).ToList();
        }

        public async Task<DelayProfile> BestForTags(HashSet<int> tagIds)
        {
            var key = "-" + tagIds.Select(v => v.ToString()).Join(",");

            var cached = _bestForTagsCache.Find(key);

            if (cached != null)
            {
                return cached;
            }

            var result = await FetchBestForTags(tagIds);
            _bestForTagsCache.Set(key, result, TimeSpan.FromSeconds(30));

            return result;
        }

        private async Task<DelayProfile> FetchBestForTags(HashSet<int> tagIds)
        {
            return (await _repo.All())
                        .Where(r => r.Tags.Intersect(tagIds).Any() || r.Tags.Empty())
                        .OrderBy(d => d.Order).First();
        }

        public async Task<List<DelayProfile>> Reorder(int id, int? afterId)
        {
            var all = (await All()).OrderBy(d => d.Order)
                           .ToList();

            var moving = all.SingleOrDefault(d => d.Id == id);
            var after = afterId.HasValue ? all.SingleOrDefault(d => d.Id == afterId) : null;

            if (moving == null)
            {
                // TODO: This should throw
                return all;
            }

            var afterOrder = GetAfterOrder(moving, after);
            var afterCount = afterOrder + 2;
            var movingOrder = moving.Order;

            foreach (var delayProfile in all)
            {
                if (delayProfile.Id == 1)
                {
                    continue;
                }

                if (delayProfile.Id == id)
                {
                    delayProfile.Order = afterOrder + 1;
                }
                else if (delayProfile.Id == after?.Id)
                {
                    delayProfile.Order = afterOrder;
                }
                else if (delayProfile.Order > afterOrder)
                {
                    delayProfile.Order = afterCount;
                    afterCount++;
                }
                else if (delayProfile.Order > movingOrder)
                {
                    delayProfile.Order--;
                }
            }

            await _repo.UpdateMany(all);

            return await All();
        }

        private int GetAfterOrder(DelayProfile moving, DelayProfile after)
        {
            if (after == null)
            {
                return 0;
            }

            if (moving.Order < after.Order)
            {
                return after.Order - 1;
            }

            return after.Order;
        }
    }
}
