using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    public interface ISceneMappingRepository : IBasicRepository<SceneMapping>
    {
        Task<List<SceneMapping>> FindByTvdbid(int tvdbId);
        Task<List<SceneMapping>> GetAllByType(string type);
    }
}
