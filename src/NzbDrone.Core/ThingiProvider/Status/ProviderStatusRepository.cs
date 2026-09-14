using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ThingiProvider.Status
{
    public interface IProviderStatusRepository<TModel> : IBasicRepository<TModel>
        where TModel : ProviderStatusBase, new()
    {
        TModel FindByProviderId(int providerId);
        void DeleteByProviderId(int providerId);
    }
}
