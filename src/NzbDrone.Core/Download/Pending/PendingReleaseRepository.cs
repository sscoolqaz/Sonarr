using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.Pending
{
    public interface IPendingReleaseRepository : IBasicRepository<PendingRelease>
    {
        Task DeleteBySeriesIds(List<int> seriesIds);
        Task<List<PendingRelease>> AllBySeriesId(int seriesId);
        Task<List<PendingRelease>> WithoutFallback();
    }
}
