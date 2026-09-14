using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Authentication
{
    public interface IUserService
    {
        Task<User> Add(string username, string password);
        Task<User> Update(User user);
        Task<User> Upsert(string username, string password);
        Task<User> FindUser();
        Task<User> FindUser(string username, string password);
        Task<User> FindUser(Guid identifier);
    }

    public class UserService : IUserService, IHandle<ApplicationStartedEvent>
    {
        private const int ITERATIONS = 10000;
        private const int SALT_SIZE = 128 / 8;
        private const int NUMBER_OF_BYTES = 256 / 8;

        private readonly IUserRepository _repo;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;

        public UserService(IUserRepository repo, IAppFolderInfo appFolderInfo, IDiskProvider diskProvider)
        {
            _repo = repo;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
        }

        public Task<User> Add(string username, string password)
        {
            var user = new User
            {
                Identifier = Guid.NewGuid(),
                Username = username.ToLowerInvariant()
            };

            SetUserHashedPassword(user, password);

            return _repo.Insert(user);
        }

        public Task<User> Update(User user)
        {
            return _repo.Update(user);
        }

        public async Task<User> Upsert(string username, string password)
        {
            var user = await FindUser();

            if (user == null)
            {
                return await Add(username, password);
            }

            if (user.Password != password)
            {
                SetUserHashedPassword(user, password);
            }

            user.Username = username.ToLowerInvariant();

            return await Update(user);
        }

        public Task<User> FindUser()
        {
            return _repo.SingleOrDefault();
        }

        public async Task<User> FindUser(string username, string password)
        {
            if (username.IsNullOrWhiteSpace() || password.IsNullOrWhiteSpace())
            {
                return null;
            }

            var user = await _repo.FindUser(username.ToLowerInvariant());

            if (user == null)
            {
                return null;
            }

            if (user.Salt.IsNullOrWhiteSpace())
            {
                // If password matches stored SHA256 hash, update to salted hash and verify.
                if (user.Password == password.SHA256Hash())
                {
                    SetUserHashedPassword(user, password);

                    return await Update(user);
                }

                return null;
            }

            if (VerifyHashedPassword(user, password))
            {
                return user;
            }

            return null;
        }

        public Task<User> FindUser(Guid identifier)
        {
            return _repo.FindUser(identifier);
        }

        private User SetUserHashedPassword(User user, string password)
        {
            var salt = GenerateSalt();

            user.Iterations = ITERATIONS;
            user.Salt = Convert.ToBase64String(salt);
            user.Password = GetHashedPassword(password, salt, ITERATIONS);

            return user;
        }

        private byte[] GenerateSalt()
        {
            var salt = new byte[SALT_SIZE];
            RandomNumberGenerator.Create().GetBytes(salt);

            return salt;
        }

        private string GetHashedPassword(string password, byte[] salt, int iterations)
        {
            return Convert.ToBase64String(KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA512,
                iterationCount: iterations,
                numBytesRequested: NUMBER_OF_BYTES));
        }

        private bool VerifyHashedPassword(User user, string password)
        {
            var salt = Convert.FromBase64String(user.Salt);
            var hashedPassword = GetHashedPassword(password, salt, user.Iterations);

            return user.Password == hashedPassword;
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide); its
        // `void Handle(TEvent message)` signature is out of scope to change. EventAggregator runs
        // handlers off the request thread via Task.Factory.StartNew and ASP.NET Core carries no
        // SynchronizationContext, so bridging here via GetAwaiter().GetResult() cannot deadlock.
        public void Handle(ApplicationStartedEvent message)
        {
            HandleApplicationStarted(message).GetAwaiter().GetResult();
        }

        private async Task HandleApplicationStarted(ApplicationStartedEvent message)
        {
            if ((await _repo.All()).Any())
            {
                return;
            }

            var configFile = _appFolderInfo.GetConfigPath();

            if (!_diskProvider.FileExists(configFile))
            {
                return;
            }

            var xDoc = XDocument.Load(configFile);
            var config = xDoc.Descendants("Config").Single();
            var usernameElement = config.Descendants("Username").FirstOrDefault();
            var passwordElement = config.Descendants("Password").FirstOrDefault();

            if (usernameElement == null || passwordElement == null)
            {
                return;
            }

            var username = usernameElement.Value;
            var password = passwordElement.Value;

            await Add(username, password);
        }
    }
}
