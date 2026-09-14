using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Download.Pending
{
    public class PendingReleaseRepository : BasicRepository<PendingRelease>, IPendingReleaseRepository
    {
        public PendingReleaseRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task DeleteBySeriesIds(List<int> seriesIds)
        {
            Delete(r => seriesIds.Contains(r.SeriesId));
            return Task.CompletedTask;
        }

        public Task<List<PendingRelease>> AllBySeriesId(int seriesId)
        {
            return Task.FromResult(Query(p => p.SeriesId == seriesId));
        }

        public Task<List<PendingRelease>> WithoutFallback()
        {
            var builder = new SqlBuilder(_database.DatabaseType)
                .InnerJoin<PendingRelease, Series>((p, s) => p.SeriesId == s.Id)
                .Where<PendingRelease>(p => p.Reason != PendingReleaseReason.Fallback);

            return Task.FromResult(Query(builder));
        }
    }
}
