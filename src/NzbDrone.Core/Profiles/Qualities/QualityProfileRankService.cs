using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileRankService
    {
        Task<double> GetRank(int? profileId, int? qualityId);
        IEnumerable<QualityProfileQualityRank> ComputeRanks(QualityProfile profile);
        Task UpdateRanksForProfile(QualityProfile profile);
        Task DeleteRanksForProfile(int profileId);
        Task SeedAll(IEnumerable<QualityProfile> profiles);
    }

    public class QualityProfileRankService : IQualityProfileRankService
    {
        private readonly IQualityProfileRankRepository _repository;
        private readonly Dictionary<int, double> _defaultRanks;
        private readonly ICached<Dictionary<(int ProfileId, int QualityId), double>> _cache;

        public QualityProfileRankService(IQualityProfileRankRepository repository, ICacheManager cacheManager)
        {
            _repository = repository;
            _defaultRanks = ComputeRanks(BuildDefaultProfile())
                .ToDictionary(r => r.QualityId, r => r.Score);
            _cache = cacheManager.GetCache<Dictionary<(int ProfileId, int QualityId), double>>(typeof(QualityProfileQualityRank), "ranks");
        }

        public async Task<double> GetRank(int? profileId, int? qualityId)
        {
            if (!qualityId.HasValue)
            {
                return -1.0;
            }

            if (profileId.HasValue)
            {
                return (await AllRanks()).TryGetValue((profileId.Value, qualityId.Value), out var rank) ? rank : -1.0;
            }

            return _defaultRanks.TryGetValue(qualityId.Value, out var defaultRank) ? defaultRank : -1.0;
        }

        public IEnumerable<QualityProfileQualityRank> ComputeRanks(QualityProfile profile)
        {
            var items = profile.Items ?? new List<QualityProfileQualityItem>();
            var singleItem = items.Count == 1;
            var denominator = Math.Max(1, items.Count - 1);

            for (var i = 0; i < items.Count; i++)
            {
                var score = singleItem ? 1.0 : (double)i / denominator;
                var item = items[i];

                if (item.Quality != null)
                {
                    yield return new QualityProfileQualityRank
                    {
                        ProfileId = profile.Id,
                        QualityId = item.Quality.Id,
                        Score = score
                    };
                    continue;
                }

                foreach (var member in item.Items ?? new List<QualityProfileQualityItem>())
                {
                    if (member.Quality == null)
                    {
                        continue;
                    }

                    yield return new QualityProfileQualityRank
                    {
                        ProfileId = profile.Id,
                        QualityId = member.Quality.Id,
                        Score = score
                    };
                }
            }
        }

        public async Task UpdateRanksForProfile(QualityProfile profile)
        {
            await _repository.ReplaceForProfile(profile.Id, ComputeRanks(profile));
            _cache.Clear();
        }

        public async Task DeleteRanksForProfile(int profileId)
        {
            await _repository.DeleteForProfile(profileId);
            _cache.Clear();
        }

        public async Task SeedAll(IEnumerable<QualityProfile> profiles)
        {
            var existingProfileIds = (await AllRanks()).Keys.Select(k => k.ProfileId).ToHashSet();
            var seeded = false;

            foreach (var profile in profiles)
            {
                if (existingProfileIds.Contains(profile.Id))
                {
                    continue;
                }

                await _repository.ReplaceForProfile(profile.Id, ComputeRanks(profile));
                seeded = true;
            }

            if (seeded)
            {
                _cache.Clear();
            }
        }

        private async Task<Dictionary<(int ProfileId, int QualityId), double>> AllRanks()
        {
            var cached = _cache.Find("all");

            if (cached != null)
            {
                return cached;
            }

            var result = (await _repository.All())
                .ToDictionary(r => (r.ProfileId, r.QualityId), r => r.Score);
            _cache.Set("all", result);

            return result;
        }

        private static QualityProfile BuildDefaultProfile()
        {
            var items = Quality.DefaultQualityDefinitions
                .GroupBy(q => q.Weight)
                .OrderBy(g => g.Key)
                .Select(group => group.Count() == 1
                    ? new QualityProfileQualityItem { Quality = group.First().Quality, Allowed = true }
                    : new QualityProfileQualityItem
                    {
                        Items = group.Select(q => new QualityProfileQualityItem { Quality = q.Quality, Allowed = true }).ToList(),
                        Allowed = true
                    })
                .ToList();

            return new QualityProfile { Items = items };
        }
    }
}
