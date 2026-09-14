using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public interface IImportListExclusionRepository : IBasicRepository<ImportListExclusion>
    {
        Task<ImportListExclusion> FindByTvdbId(int tvdbId);
    }
}
