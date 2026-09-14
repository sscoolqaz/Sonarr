using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public interface IDownloadClientFactory : IProviderFactory<IDownloadClient, DownloadClientDefinition>
    {
        Task<List<IDownloadClient>> DownloadHandlingEnabled(bool filterBlockedClients = true);
        Task<DownloadClientDefinition> ResolveDownloadClient(int? id, string name);
    }

    public class DownloadClientFactory : ProviderFactory<IDownloadClient, DownloadClientDefinition>, IDownloadClientFactory
    {
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly Logger _logger;

        public DownloadClientFactory(IDownloadClientStatusService downloadClientStatusService,
                                     IDownloadClientRepository providerRepository,
                                     IEnumerable<IDownloadClient> providers,
                                     IServiceProvider container,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
            : base(providerRepository, providers, container, eventAggregator, logger)
        {
            _downloadClientStatusService = downloadClientStatusService;
            _logger = logger;
        }

        protected override async Task<List<DownloadClientDefinition>> Active()
        {
            return (await base.Active()).Where(c => c.Enable).ToList();
        }

        public override void SetProviderCharacteristics(IDownloadClient provider, DownloadClientDefinition definition)
        {
            base.SetProviderCharacteristics(provider, definition);

            definition.Protocol = provider.Protocol;
        }

        public async Task<List<IDownloadClient>> DownloadHandlingEnabled(bool filterBlockedClients = true)
        {
            var enabledClients = await GetAvailableProviders();

            if (filterBlockedClients)
            {
                return await FilterBlockedClients(enabledClients);
            }

            return enabledClients.ToList();
        }

        public async Task<DownloadClientDefinition> ResolveDownloadClient(int? id, string name)
        {
            var all = await All();
            var clientByName = name.IsNullOrWhiteSpace() ? null : all.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name));
            var clientById = id is > 0 ? all.FirstOrDefault(c => c.Id == id.Value) : null;

            if (id is > 0 && clientById == null)
            {
                throw new ResolveDownloadClientException("Download client with ID '{0}' could not be found", id.Value);
            }

            if (name.IsNotNullOrWhiteSpace() && clientByName == null)
            {
                throw new ResolveDownloadClientException("Download client with name '{0}' could not be found", name);
            }

            if (clientByName == null && clientById == null)
            {
                return null;
            }

            if (clientByName != null && clientById != null && clientByName.Id != clientById.Id)
            {
                throw new ResolveDownloadClientException("Download client with name '{0}' does not match download client with ID '{1}'", name, id.Value);
            }

            var client = clientById ?? clientByName;

            if (!client.Enable)
            {
                throw new ResolveDownloadClientException("Download client '{0}' ({1}) is not enabled", client.Name, id);
            }

            return client;
        }

        private async Task<List<IDownloadClient>> FilterBlockedClients(IEnumerable<IDownloadClient> clients)
        {
            var blockedClients = (await _downloadClientStatusService.GetBlockedProviders()).ToDictionary(v => v.ProviderId, v => v);
            var result = new List<IDownloadClient>();

            foreach (var client in clients)
            {
                if (blockedClients.TryGetValue(client.Definition.Id, out var downloadClientStatus))
                {
                    _logger.Debug("Temporarily ignoring download client {0} till {1} due to recent failures.", client.Definition.Name, downloadClientStatus.DisabledTill.Value.ToLocalTime());
                    continue;
                }

                result.Add(client);
            }

            return result;
        }

        public override async Task<ValidationResult> Test(DownloadClientDefinition definition)
        {
            var result = await base.Test(definition);

            if (definition.Id == 0)
            {
                return result;
            }

            if (result == null || result.IsValid)
            {
                await _downloadClientStatusService.RecordSuccess(definition.Id);
            }
            else
            {
                await _downloadClientStatusService.RecordFailure(definition.Id);
            }

            return result;
        }
    }
}
