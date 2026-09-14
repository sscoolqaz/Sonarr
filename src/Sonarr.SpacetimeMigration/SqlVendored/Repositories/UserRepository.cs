using System;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Authentication
{
    public class UserRepository : BasicRepository<User>, IUserRepository
    {
        public UserRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<User> FindUser(string username)
        {
            return Task.FromResult(Query(x => x.Username == username).SingleOrDefault());
        }

        public Task<User> FindUser(Guid identifier)
        {
            return Task.FromResult(Query(x => x.Identifier == identifier).SingleOrDefault());
        }
    }
}
