using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Profiles.Releases
{
    public interface IReleaseProfileService
    {
        Task<List<ReleaseProfile>> All();
        Task<List<ReleaseProfile>> AllExcludedForTag(int tagId);
        Task<List<ReleaseProfile>> AllForTag(int tagId);
        Task<List<ReleaseProfile>> AllForTags(HashSet<int> tagIds);
        Task<List<ReleaseProfile>> EnabledForTags(HashSet<int> tagIds, int indexerId);
        Task<ReleaseProfile> Get(int id);
        Task Delete(int id);
        Task<ReleaseProfile> Add(ReleaseProfile restriction);
        Task<ReleaseProfile> Update(ReleaseProfile restriction);
    }

    public class ReleaseProfileService : IReleaseProfileService
    {
        private readonly IRestrictionRepository _repo;

        public ReleaseProfileService(IRestrictionRepository repo)
        {
            _repo = repo;
        }

        public async Task<List<ReleaseProfile>> All()
        {
            var all = (await _repo.All()).ToList();

            return all;
        }

        public async Task<List<ReleaseProfile>> AllExcludedForTag(int tagId)
        {
            return (await _repo.All()).Where(r => r.ExcludedTags.Contains(tagId)).ToList();
        }

        public async Task<List<ReleaseProfile>> AllForTag(int tagId)
        {
            return (await _repo.All()).Where(r => r.Tags.Contains(tagId)).ToList();
        }

        public async Task<List<ReleaseProfile>> AllForTags(HashSet<int> tagIds)
        {
            return (await _repo.All()).Where(r => (r.Tags.Intersect(tagIds).Any() || r.Tags.Empty()) && !r.ExcludedTags.Intersect(tagIds).Any()).ToList();
        }

        public async Task<List<ReleaseProfile>> EnabledForTags(HashSet<int> tagIds, int indexerId)
        {
            return (await AllForTags(tagIds))
                .Where(r => r.Enabled)
                .Where(r => r.IndexerIds.Contains(indexerId) || r.IndexerIds.Empty())
                .ToList();
        }

        public async Task<ReleaseProfile> Get(int id)
        {
            return await _repo.Get(id);
        }

        public async Task Delete(int id)
        {
            await _repo.Delete(id);
        }

        public async Task<ReleaseProfile> Add(ReleaseProfile restriction)
        {
            return await _repo.Insert(restriction);
        }

        public async Task<ReleaseProfile> Update(ReleaseProfile restriction)
        {
            return await _repo.Update(restriction);
        }
    }
}
