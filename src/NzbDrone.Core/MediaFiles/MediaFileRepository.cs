using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileRepository : IBasicRepository<EpisodeFile>
    {
        Task<List<EpisodeFile>> GetFilesBySeries(int seriesId);
        Task<List<EpisodeFile>> GetFilesBySeriesIds(List<int> seriesIds);
        Task<List<EpisodeFile>> GetFilesBySeason(int seriesId, int seasonNumber);
        Task<List<EpisodeFile>> GetFilesWithoutMediaInfo();
        Task<List<EpisodeFile>> GetFilesWithRelativePath(int seriesId, string relativePath);
        Task DeleteForSeries(List<int> seriesIds);
    }
}
