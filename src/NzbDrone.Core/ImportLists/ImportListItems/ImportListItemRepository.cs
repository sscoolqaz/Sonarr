using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.ImportListItems
{
    public interface IImportListItemRepository : IBasicRepository<ImportListItemInfo>
    {
        Task<List<ImportListItemInfo>> GetAllForLists(List<int> listIds);
    }
}
