using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerStatusService : IProviderStatusServiceBase<IndexerStatus>
    {
        Task<ReleaseInfo> GetLastRssSyncReleaseInfo(int indexerId);

        Task UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo);
    }

    public class IndexerStatusService : ProviderStatusServiceBase<IIndexer, IndexerStatus>, IIndexerStatusService
    {
        public IndexerStatusService(IIndexerStatusRepository providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, Logger logger)
            : base(providerStatusRepository, eventAggregator, runtimeInfo, logger)
        {
        }

        public async Task<ReleaseInfo> GetLastRssSyncReleaseInfo(int indexerId)
        {
            return (await GetProviderStatus(indexerId)).LastRssSyncReleaseInfo;
        }

        public async Task UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo)
        {
            await _syncRoot.WaitAsync();

            try
            {
                var status = await GetProviderStatus(indexerId);

                status.LastRssSyncReleaseInfo = releaseInfo;

                await _providerStatusRepository.Upsert(status);
            }
            finally
            {
                _syncRoot.Release();
            }
        }
    }
}
