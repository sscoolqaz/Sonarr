using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Extras.Files
{
    public interface IExtraFileRepository<TExtraFile> : IBasicRepository<TExtraFile>
        where TExtraFile : ExtraFile, new()
    {
        Task DeleteForSeriesIds(List<int> seriesIds);
        Task DeleteForSeason(int seriesId, int seasonNumber);
        Task DeleteForEpisodeFile(int episodeFileId);
        Task<List<TExtraFile>> GetFilesBySeries(int seriesId);
        Task<List<TExtraFile>> GetFilesBySeason(int seriesId, int seasonNumber);
        Task<List<TExtraFile>> GetFilesByEpisodeFile(int episodeFileId);
        Task<TExtraFile> FindByPath(int seriesId, string path);
    }
}
