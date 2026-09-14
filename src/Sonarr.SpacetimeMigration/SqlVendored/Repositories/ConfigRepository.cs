using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Configuration
{
    public class ConfigRepository : BasicRepository<Config>, IConfigRepository
    {
        public ConfigRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<Config> Get(string key)
        {
            return Task.FromResult(Query(c => c.Key == key).SingleOrDefault());
        }

        public Task<Config> Upsert(string key, string value)
        {
            var dbValue = Get(key).GetAwaiter().GetResult();

            if (dbValue == null)
            {
                return Insert(new Config { Key = key, Value = value });
            }

            dbValue.Value = value;

            return Update(dbValue);
        }
    }
}
