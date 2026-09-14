using System;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Authentication
{
    public interface IUserRepository : IBasicRepository<User>
    {
        Task<User> FindUser(string username);
        Task<User> FindUser(Guid identifier);
    }
}
