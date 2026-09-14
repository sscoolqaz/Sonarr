using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileRepository : IBasicRepository<QualityProfile>
    {
        bool Exists(int id);
    }
}
