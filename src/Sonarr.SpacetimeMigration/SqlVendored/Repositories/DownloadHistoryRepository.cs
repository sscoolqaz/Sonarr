using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.History
{
    public class DownloadHistoryRepository : BasicRepository<DownloadHistory>, IDownloadHistoryRepository
    {
        public DownloadHistoryRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<List<DownloadHistory>> FindByDownloadId(string downloadId)
        {
            return Task.FromResult(Query(h => h.DownloadId == downloadId).OrderByDescending(h => h.Date).ToList());
        }

        public Task DeleteBySeriesIds(List<int> seriesIds)
        {
            Delete(r => seriesIds.Contains(r.SeriesId));
            return Task.CompletedTask;
        }
    }
}
