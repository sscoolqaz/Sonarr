using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    public class ImportListRepository : ProviderRepository<ImportListDefinition>, IImportListRepository
    {
        public ImportListRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task UpdateSettings(ImportListDefinition model)
        {
            SetFields(model, m => m.Settings).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
    }
}
