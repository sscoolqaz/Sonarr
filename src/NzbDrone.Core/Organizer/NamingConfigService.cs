using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Organizer
{
    public interface INamingConfigService
    {
        Task<NamingConfig> GetConfig();
        Task Save(NamingConfig namingConfig);
    }

    public class NamingConfigService : INamingConfigService
    {
        private readonly INamingConfigRepository _repository;

        // NOTE: was a plain `lock (_repository)`; converted to SemaphoreSlim since the critical
        // section now needs to `await` repository calls, which C#'s `lock` forbids.
        private readonly SemaphoreSlim _syncRoot = new SemaphoreSlim(1, 1);

        public NamingConfigService(INamingConfigRepository repository)
        {
            _repository = repository;
        }

        public async Task<NamingConfig> GetConfig()
        {
            var config = await _repository.SingleOrDefault();

            if (config == null)
            {
                await _syncRoot.WaitAsync();

                try
                {
                    config = await _repository.SingleOrDefault();

                    if (config == null)
                    {
                        await _repository.Insert(NamingConfig.Default);
                        config = await _repository.Single();
                    }
                }
                finally
                {
                    _syncRoot.Release();
                }
            }

            return config;
        }

        public async Task Save(NamingConfig namingConfig)
        {
            await _repository.Upsert(namingConfig);
        }
    }
}
