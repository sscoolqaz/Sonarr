using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileRankRepository : IBasicRepository<QualityProfileQualityRank>
    {
        void ReplaceForProfile(int profileId, IEnumerable<QualityProfileQualityRank> ranks);
        void DeleteForProfile(int profileId);
    }
}
