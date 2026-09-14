using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileRepository : IBasicRepository<QualityProfile>
    {
        Task<bool> Exists(int id);
    }
}
