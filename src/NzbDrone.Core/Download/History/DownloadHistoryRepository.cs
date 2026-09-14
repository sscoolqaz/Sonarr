using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryRepository : IBasicRepository<DownloadHistory>
    {
        Task<List<DownloadHistory>> FindByDownloadId(string downloadId);
        Task DeleteBySeriesIds(List<int> seriesIds);
    }
}
