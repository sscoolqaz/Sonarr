using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Extras.Files
{
    public class ExtraFileRepository<TExtraFile> : BasicRepository<TExtraFile>, IExtraFileRepository<TExtraFile>
        where TExtraFile : ExtraFile, new()
    {
        public ExtraFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task DeleteForSeriesIds(List<int> seriesIds)
        {
            Delete(c => seriesIds.Contains(c.SeriesId));
            return Task.CompletedTask;
        }

        public Task DeleteForSeason(int seriesId, int seasonNumber)
        {
            Delete(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber);
            return Task.CompletedTask;
        }

        public Task DeleteForEpisodeFile(int episodeFileId)
        {
            Delete(c => c.EpisodeFileId == episodeFileId);
            return Task.CompletedTask;
        }

        public Task<List<TExtraFile>> GetFilesBySeries(int seriesId)
        {
            return Task.FromResult(Query(c => c.SeriesId == seriesId));
        }

        public Task<List<TExtraFile>> GetFilesBySeason(int seriesId, int seasonNumber)
        {
            return Task.FromResult(Query(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber));
        }

        public Task<List<TExtraFile>> GetFilesByEpisodeFile(int episodeFileId)
        {
            return Task.FromResult(Query(c => c.EpisodeFileId == episodeFileId));
        }

        public Task<TExtraFile> FindByPath(int seriesId, string path)
        {
            return Task.FromResult(Query(c => c.SeriesId == seriesId && c.RelativePath == path).SingleOrDefault());
        }
    }
}
