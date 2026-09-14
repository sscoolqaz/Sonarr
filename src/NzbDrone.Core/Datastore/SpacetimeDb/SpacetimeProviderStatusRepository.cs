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
        where TStdbRow : class, SpacetimeDB.BSATN.IStructuralReadWrite, new()
    {
        protected SpacetimeProviderStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        public TModel FindByProviderId(int providerId) =>
            Query(t => t.Iter().Select(ToModel).ToList()).SingleOrDefault(m => m.ProviderId == providerId);

        public void DeleteByProviderId(int providerId)
        {
            foreach (var row in All().Where(m => m.ProviderId == providerId).ToList())
            {
                Delete(row.Id);
            }
        }
    }
}
