using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    public interface IImportListFactory : IProviderFactory<IImportList, ImportListDefinition>
    {
        Task<List<IImportList>> AutomaticAddEnabled(bool filterBlockedImportLists = true);
    }

    public class ImportListFactory : ProviderFactory<IImportList, ImportListDefinition>, IImportListFactory
    {
        private readonly IImportListStatusService _importListStatusService;
        private readonly Logger _logger;

        public ImportListFactory(IImportListStatusService importListStatusService,
                              IImportListRepository providerRepository,
                              IEnumerable<IImportList> providers,
                              IServiceProvider container,
                              IEventAggregator eventAggregator,
                              Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _importListStatusService = importListStatusService;
            _logger = logger;
        }

        protected override async Task<List<ImportListDefinition>> Active()
        {
            return (await base.Active()).Where(c => c.Enable).ToList();
        }

        public override void SetProviderCharacteristics(IImportList provider, ImportListDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.ListType = provider.ListType;
            definition.MinRefreshInterval = provider.MinRefreshInterval;
        }

        public async Task<List<IImportList>> AutomaticAddEnabled(bool filterBlockedImportLists = true)
        {
            var enabledImportLists = (await GetAvailableProviders()).Where(n => ((ImportListDefinition)n.Definition).EnableAutomaticAdd);

            if (filterBlockedImportLists)
            {
                return await FilterBlockedImportLists(enabledImportLists);
            }

            return enabledImportLists.ToList();
        }

        private async Task<List<IImportList>> FilterBlockedImportLists(IEnumerable<IImportList> importLists)
        {
            var blockedImportLists = (await _importListStatusService.GetBlockedProviders()).ToDictionary(v => v.ProviderId, v => v);
            var result = new List<IImportList>();

            foreach (var importList in importLists)
            {
                if (blockedImportLists.TryGetValue(importList.Definition.Id, out var blockedImportListStatus))
                {
                    _logger.Debug("Temporarily ignoring import list {0} till {1} due to recent failures.", importList.Definition.Name, blockedImportListStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                result.Add(importList);
            }

            return result;
        }

        public override async Task<ValidationResult> Test(ImportListDefinition definition)
        {
            var result = await base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                await _importListStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                await _importListStatusService.RecordFailure(definition.Id);
            }

            return result;
        }
    }
}
