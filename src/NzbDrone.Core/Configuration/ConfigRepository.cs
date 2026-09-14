using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Configuration
{
    public interface IConfigRepository : IBasicRepository<Config>
    {
        Task<Config> Get(string key);
        Task<Config> Upsert(string key, string value);
    }
}
