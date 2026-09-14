using System.Threading.Tasks;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.ImportLists
{
    public interface IImportListRepository : IProviderRepository<ImportListDefinition>
    {
        Task UpdateSettings(ImportListDefinition model);
    }
}
