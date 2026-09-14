using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileRankRepository : IBasicRepository<QualityProfileQualityRank>
    {
        Task ReplaceForProfile(int profileId, IEnumerable<QualityProfileQualityRank> ranks);
        Task DeleteForProfile(int profileId);
    }
}
