using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerFactory : IProviderFactory<IIndexer, IndexerDefinition>
    {
        Task<List<IIndexer>> RssEnabled(bool filterBlockedIndexers = true);
        Task<List<IIndexer>> AutomaticSearchEnabled(bool filterBlockedIndexers = true);
        Task<List<IIndexer>> InteractiveSearchEnabled(bool filterBlockedIndexers = true);
        Task<IndexerDefinition> FindByName(string name);
        Task<IndexerDefinition> ResolveIndexer(int? id, string name);
    }

    public class IndexerFactory : ProviderFactory<IIndexer, IndexerDefinition>, IIndexerFactory
    {
        private readonly IIndexerRepository _indexerRepository;
        private readonly IIndexerStatusService _indexerStatusService;
        private readonly Logger _logger;

        public IndexerFactory(IIndexerStatusService indexerStatusService,
                              IIndexerRepository providerRepository,
                              IEnumerable<IIndexer> providers,
                              IServiceProvider container,
                              IEventAggregator eventAggregator,
                              Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _indexerRepository = providerRepository;
            _indexerStatusService = indexerStatusService;
            _logger = logger;
        }

        protected override async Task<List<IndexerDefinition>> Active()
        {
            return (await base.Active()).Where(c => c.Enable).ToList();
        }

        public override void SetProviderCharacteristics(IIndexer provider, IndexerDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.Protocol = provider.Protocol;
            definition.SupportsRss = provider.SupportsRss;
            definition.SupportsSearch = provider.SupportsSearch;
        }

        public async Task<List<IIndexer>> RssEnabled(bool filterBlockedIndexers = true)
        {
            var enabledIndexers = (await GetAvailableProviders()).Where(n => ((IndexerDefinition)n.Definition).EnableRss);

            if (filterBlockedIndexers)
            {
                return await FilterBlockedIndexers(enabledIndexers);
            }

            return enabledIndexers.ToList();
        }

        public async Task<List<IIndexer>> AutomaticSearchEnabled(bool filterBlockedIndexers = true)
        {
            var enabledIndexers = (await GetAvailableProviders()).Where(n => ((IndexerDefinition)n.Definition).EnableAutomaticSearch);

            if (filterBlockedIndexers)
            {
                return await FilterBlockedIndexers(enabledIndexers);
            }

            return enabledIndexers.ToList();
        }

        public async Task<List<IIndexer>> InteractiveSearchEnabled(bool filterBlockedIndexers = true)
        {
            var enabledIndexers = (await GetAvailableProviders()).Where(n => ((IndexerDefinition)n.Definition).EnableInteractiveSearch);

            if (filterBlockedIndexers)
            {
                return await FilterBlockedIndexers(enabledIndexers);
            }

            return enabledIndexers.ToList();
        }

        public async Task<IndexerDefinition> FindByName(string name)
        {
            return await _indexerRepository.FindByName(name);
        }

        public async Task<IndexerDefinition> ResolveIndexer(int? id, string name)
        {
            var all = await All();
            var clientByName = name.IsNullOrWhiteSpace() ? null : all.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
            var clientById = id is > 0 ? all.FirstOrDefault(c => c.Id == id.Value) : null;

            if (id is > 0 && clientById == null)
            {
                throw new ResolveIndexerException("Indexer with ID '{0}' could not be found", id.Value);
            }

            if (name.IsNotNullOrWhiteSpace() && clientByName == null)
            {
                throw new ResolveIndexerException("Indexer with name '{0}' could not be found", name);
            }

            if (clientByName == null && clientById == null)
            {
                return null;
            }

            if (clientByName != null && clientById != null && clientByName.Id != clientById.Id)
            {
                throw new ResolveIndexerException("Indexer with name '{0}' does not match indexer with ID '{1}'", name, id.Value);
            }

            return clientById ?? clientByName;
        }

        private async Task<List<IIndexer>> FilterBlockedIndexers(IEnumerable<IIndexer> indexers)
        {
            var blockedIndexers = (await _indexerStatusService.GetBlockedProviders()).ToDictionary(v => v.ProviderId, v => v);
            var result = new List<IIndexer>();

            foreach (var indexer in indexers)
            {
                if (blockedIndexers.TryGetValue(indexer.Definition.Id, out var blockedIndexerStatus))
                {
                    _logger.Debug("Temporarily ignoring indexer {0} till {1} due to recent failures.", indexer.Definition.Name, blockedIndexerStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                result.Add(indexer);
            }

            return result;
        }

        public override async Task<ValidationResult> Test(IndexerDefinition definition)
        {
            var result = await base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                await _indexerStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                await _indexerStatusService.RecordFailure(definition.Id);
            }

            return result;
        }
    }
}
