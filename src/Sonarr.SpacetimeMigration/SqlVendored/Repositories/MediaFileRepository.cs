using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MediaFiles
{
    public class MediaFileRepository : BasicRepository<EpisodeFile>, IMediaFileRepository
    {
        public MediaFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<List<EpisodeFile>> GetFilesBySeries(int seriesId)
        {
            return Task.FromResult(Query(c => c.SeriesId == seriesId).ToList());
        }

        public Task<List<EpisodeFile>> GetFilesBySeriesIds(List<int> seriesIds)
        {
            return Task.FromResult(Query(c => seriesIds.Contains(c.SeriesId)).ToList());
        }

        public Task<List<EpisodeFile>> GetFilesBySeason(int seriesId, int seasonNumber)
        {
            return Task.FromResult(Query(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber).ToList());
        }

        public Task<List<EpisodeFile>> GetFilesWithoutMediaInfo()
        {
            return Task.FromResult(Query(c => c.MediaInfo == null).ToList());
        }

        public Task<List<EpisodeFile>> GetFilesWithRelativePath(int seriesId, string relativePath)
        {
            return Task.FromResult(Query(c => c.SeriesId == seriesId && c.RelativePath == relativePath)
                        .ToList());
        }

        public Task DeleteForSeries(List<int> seriesIds)
        {
            Delete(x => seriesIds.Contains(x.SeriesId));
            return Task.CompletedTask;
        }
    }
}
