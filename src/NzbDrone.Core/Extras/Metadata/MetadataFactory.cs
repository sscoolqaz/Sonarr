using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Extras.Metadata
{
    public interface IMetadataFactory : IProviderFactory<IMetadata, MetadataDefinition>
    {
        Task<List<IMetadata>> Enabled();
    }

    public class MetadataFactory : ProviderFactory<IMetadata, MetadataDefinition>, IMetadataFactory
    {
        private readonly IMetadataRepository _providerRepository;

        public MetadataFactory(IMetadataRepository providerRepository, IEnumerable<IMetadata> providers, IServiceProvider container, IEventAggregator eventAggregator, Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _providerRepository = providerRepository;
        }

        protected override async Task InitializeProviders()
        {
            var definitions = new List<MetadataDefinition>();

            foreach (var provider in _providers)
            {
                definitions.Add(new MetadataDefinition
                {
                    Enable = false,
                    Name = provider.Name,
                    Implementation = provider.GetType().Name,
                    Settings = (IProviderConfig)Activator.CreateInstance(provider.ConfigContract)
                });
            }

            var currentProviders = await All();

            var newProviders = definitions.Where(def => currentProviders.All(c => c.Implementation != def.Implementation)).ToList();

            if (newProviders.Any())
            {
                await _providerRepository.InsertMany(newProviders.Cast<MetadataDefinition>().ToList());
            }
        }

        public async Task<List<IMetadata>> Enabled()
        {
            return (await GetAvailableProviders()).Where(n => ((MetadataDefinition)n.Definition).Enable).ToList();
        }
    }
}
