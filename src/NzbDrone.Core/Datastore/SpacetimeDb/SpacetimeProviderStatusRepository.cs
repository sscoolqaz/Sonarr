using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Generic base mirroring the real ProviderStatusRepository&lt;TModel&gt; (shared by
    /// Indexer/DownloadClient/ImportList/NotificationStatus).
    /// </summary>
    public abstract class SpacetimeProviderStatusRepository<TModel, TStdbRow> : SpacetimeBasicRepository<TModel, TStdbRow>, IProviderStatusRepository<TModel>
        where TModel : ProviderStatusBase, new()
    {
        protected SpacetimeProviderStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        public TModel FindByProviderId(int providerId) =>
            RemoteQuery($"WHERE ProviderId = {providerId}").Select(ToModel).SingleOrDefault();

        public void DeleteByProviderId(int providerId)
        {
            foreach (var row in All().Where(m => m.ProviderId == providerId).ToList())
            {
                Delete(row.Id);
            }
        }
    }
}
