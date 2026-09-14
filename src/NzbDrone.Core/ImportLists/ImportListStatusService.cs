using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.ImportLists
{
    public interface IImportListStatusService : IProviderStatusServiceBase<ImportListStatus>
    {
        Task<ImportListStatus> GetListStatus(int importListId);

        Task UpdateListSyncStatus(int importListId, bool removedItems);
        Task MarkListsAsCleaned();
    }

    public class ImportListStatusService : ProviderStatusServiceBase<IImportList, ImportListStatus>, IImportListStatusService
    {
        public ImportListStatusService(IImportListStatusRepository providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, Logger logger)
            : base(providerStatusRepository, eventAggregator, runtimeInfo, logger)
        {
        }

        public async Task<ImportListStatus> GetListStatus(int importListId)
        {
            return await GetProviderStatus(importListId);
        }

        public async Task UpdateListSyncStatus(int importListId, bool removedItems)
        {
            await _syncRoot.WaitAsync();

            try
            {
                var status = await GetProviderStatus(importListId);

                status.LastInfoSync = DateTime.UtcNow;
                status.HasRemovedItemSinceLastClean |= removedItems;

                await _providerStatusRepository.Upsert(status);
            }
            finally
            {
                _syncRoot.Release();
            }
        }

        public async Task MarkListsAsCleaned()
        {
            await _syncRoot.WaitAsync();

            try
            {
                var toUpdate = new List<ImportListStatus>();

                foreach (var status in await _providerStatusRepository.All())
                {
                    status.HasRemovedItemSinceLastClean = false;
                    toUpdate.Add(status);
                }

                await _providerStatusRepository.UpdateMany(toUpdate);
            }
            finally
            {
                _syncRoot.Release();
            }
        }
    }
}
