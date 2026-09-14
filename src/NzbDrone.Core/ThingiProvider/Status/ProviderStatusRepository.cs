using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ThingiProvider.Status
{
    public interface IProviderStatusRepository<TModel> : IBasicRepository<TModel>
        where TModel : ProviderStatusBase, new()
    {
        Task<TModel> FindByProviderId(int providerId);
        Task DeleteByProviderId(int providerId);
    }
}
