using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Events;

namespace NzbDrone.Core.ThingiProvider
{
    public abstract class ProviderFactory<TProvider, TProviderDefinition> : IProviderFactory<TProvider, TProviderDefinition>, IHandle<ApplicationStartedEvent>
        where TProviderDefinition : ProviderDefinition, new()
        where TProvider : IProvider
    {
        private readonly IProviderRepository<TProviderDefinition> _providerRepository;
        private readonly IServiceProvider _container;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        protected readonly List<TProvider> _providers;

        protected ProviderFactory(IProviderRepository<TProviderDefinition> providerRepository,
                                  IEnumerable<TProvider> providers,
                                  IServiceProvider container,
                                  IEventAggregator eventAggregator,
                                  Logger logger)
        {
            _providerRepository = providerRepository;
            _container = container;
            _eventAggregator = eventAggregator;
            _providers = providers.ToList();
            _logger = logger;
        }

        public async Task<List<TProviderDefinition>> All()
        {
            return (await _providerRepository.All()).ToList();
        }

        public IEnumerable<TProviderDefinition> GetDefaultDefinitions()
        {
            foreach (var provider in _providers)
            {
                var definition = provider.DefaultDefinitions
                    .OfType<TProviderDefinition>()
                    .FirstOrDefault(v => v.Name == null || v.Name == provider.GetType().Name);

                if (definition == null)
                {
                    definition = new TProviderDefinition()
                    {
                        Name = string.Empty,
                        ConfigContract = provider.ConfigContract.Name,
                        Implementation = provider.GetType().Name,
                        Settings = (IProviderConfig)Activator.CreateInstance(provider.ConfigContract)
                    };
                }

                SetProviderCharacteristics(provider, definition);

                yield return definition;
            }
        }

        public IEnumerable<TProviderDefinition> GetPresetDefinitions(TProviderDefinition providerDefinition)
        {
            var provider = _providers.First(v => v.GetType().Name == providerDefinition.Implementation);

            var definitions = provider.DefaultDefinitions
                   .OfType<TProviderDefinition>()
                   .Where(v => v.Name != null && v.Name != provider.GetType().Name)
                   .ToList();

            return definitions;
        }

        public virtual Task<ValidationResult> Test(TProviderDefinition definition)
        {
            return Task.FromResult(GetInstance(definition).Test());
        }

        public object RequestAction(TProviderDefinition definition, string action, IDictionary<string, string> query)
        {
            return GetInstance(definition).RequestAction(action, query);
        }

        public async Task<List<TProvider>> GetAvailableProviders()
        {
            return (await Active()).Select(GetInstance).ToList();
        }

        public async Task<bool> Exists(int id)
        {
            return await _providerRepository.Find(id) != null;
        }

        public async Task<TProviderDefinition> Get(int id)
        {
            return await _providerRepository.Get(id);
        }

        public async Task<IEnumerable<TProviderDefinition>> Get(IEnumerable<int> ids)
        {
            return await _providerRepository.Get(ids);
        }

        public async Task<TProviderDefinition> Find(int id)
        {
            return await _providerRepository.Find(id);
        }

        public virtual async Task<TProviderDefinition> Create(TProviderDefinition definition)
        {
            var result = await _providerRepository.Insert(definition);
            _eventAggregator.PublishEvent(new ProviderAddedEvent<TProvider>(result));

            return result;
        }

        public virtual async Task Update(TProviderDefinition definition)
        {
            await _providerRepository.Update(definition);
            _eventAggregator.PublishEvent(new ProviderUpdatedEvent<TProvider>(definition));
        }

        public virtual async Task<IEnumerable<TProviderDefinition>> Update(IEnumerable<TProviderDefinition> definitions)
        {
            var definitionsList = definitions.ToList();

            await _providerRepository.UpdateMany(definitionsList);

            foreach (var definition in definitionsList)
            {
                _eventAggregator.PublishEvent(new ProviderUpdatedEvent<TProvider>(definition));
            }

            return definitionsList;
        }

        public async Task Delete(int id)
        {
            await _providerRepository.Delete(id);
            _eventAggregator.PublishEvent(new ProviderDeletedEvent<TProvider>(id));
        }

        public async Task Delete(IEnumerable<int> ids)
        {
            var idsList = ids.ToList();

            await _providerRepository.DeleteMany(idsList);

            foreach (var id in idsList)
            {
                _eventAggregator.PublishEvent(new ProviderDeletedEvent<TProvider>(id));
            }
        }

        public TProvider GetInstance(TProviderDefinition definition)
        {
            var type = GetImplementation(definition);
            var instance = (TProvider)_container.GetRequiredService(type);
            instance.Definition = definition;
            SetProviderCharacteristics(instance, definition);
            return instance;
        }

        private Type GetImplementation(TProviderDefinition definition)
        {
            return _providers.Select(c => c.GetType()).SingleOrDefault(c => c.Name.Equals(definition.Implementation, StringComparison.InvariantCultureIgnoreCase));
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface implemented by 50+ handlers across
        // the app; changing its `void Handle(TEvent message)` signature to Task is out of scope for
        // this pass (see task instructions). ApplicationStartedEvent fires once at startup (not on
        // an ASP.NET Core request thread, which has no SynchronizationContext by default), so
        // blocking here via GetAwaiter().GetResult() is a documented, deliberate boundary rather
        // than a silently-scattered blocking call.
        public void Handle(ApplicationStartedEvent message)
        {
            _logger.Debug("Initializing Providers. Count {0}", _providers.Count);

            RemoveMissingImplementations().GetAwaiter().GetResult();

            InitializeProviders().GetAwaiter().GetResult();
        }

        protected virtual Task InitializeProviders()
        {
            return Task.CompletedTask;
        }

        protected virtual async Task<List<TProviderDefinition>> Active()
        {
            return (await All()).Where(c => c.Settings.Validate().IsValid).ToList();
        }

        public void SetProviderCharacteristics(TProviderDefinition definition)
        {
            GetInstance(definition);
        }

        public virtual void SetProviderCharacteristics(TProvider provider, TProviderDefinition definition)
        {
            definition.ImplementationName = provider.Name;
            definition.Message = provider.Message;
        }

        private async Task RemoveMissingImplementations()
        {
            var storedProvider = await _providerRepository.All();

            foreach (var invalidDefinition in storedProvider.Where(def => GetImplementation(def) == null))
            {
                _logger.Warn("Removing {0}", invalidDefinition.Name);
                await _providerRepository.Delete(invalidDefinition);
            }
        }

        public async Task<List<TProviderDefinition>> AllForTag(int tagId)
        {
            return (await All()).Where(p => p.Tags.Contains(tagId))
                        .ToList();
        }
    }
}
