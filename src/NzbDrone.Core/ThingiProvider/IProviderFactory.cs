using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;

namespace NzbDrone.Core.ThingiProvider
{
    public interface IProviderFactory<TProvider, TProviderDefinition>
        where TProviderDefinition : ProviderDefinition, new()
        where TProvider : IProvider
    {
        Task<List<TProviderDefinition>> All();
        Task<List<TProvider>> GetAvailableProviders();
        Task<bool> Exists(int id);
        Task<TProviderDefinition> Find(int id);
        Task<TProviderDefinition> Get(int id);
        Task<IEnumerable<TProviderDefinition>> Get(IEnumerable<int> ids);
        Task<TProviderDefinition> Create(TProviderDefinition definition);
        Task Update(TProviderDefinition definition);
        Task<IEnumerable<TProviderDefinition>> Update(IEnumerable<TProviderDefinition> definitions);
        Task Delete(int id);
        Task Delete(IEnumerable<int> ids);
        IEnumerable<TProviderDefinition> GetDefaultDefinitions();
        IEnumerable<TProviderDefinition> GetPresetDefinitions(TProviderDefinition providerDefinition);
        void SetProviderCharacteristics(TProviderDefinition definition);
        void SetProviderCharacteristics(TProvider provider, TProviderDefinition definition);
        TProvider GetInstance(TProviderDefinition definition);
        Task<ValidationResult> Test(TProviderDefinition definition);
        object RequestAction(TProviderDefinition definition, string action, IDictionary<string, string> query);
        Task<List<TProviderDefinition>> AllForTag(int tagId);
    }
}
