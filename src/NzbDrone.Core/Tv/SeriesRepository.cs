using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tv
{
    public interface ISeriesRepository : IBasicRepository<Series>
    {
        Task<bool> SeriesPathExists(string path);
        Task<Series> FindByTitle(string cleanTitle);
        Task<Series> FindByTitle(string cleanTitle, int year);
        Task<List<Series>> FindByTitleInexact(string cleanTitle);
        Task<Series> FindByTvdbId(int tvdbId);
        Task<Series> FindByTvRageId(int tvRageId);
        Task<Series> FindByImdbId(string imdbId);
        Task<Series> FindByPath(string path);
        Task<Dictionary<int, int>> AllSeriesTvdbIds();
        Task<Dictionary<int, string>> AllSeriesPaths();
        Task<Dictionary<int, List<int>>> AllSeriesTags();
        Task<Dictionary<int, int>> AllSeriesQualityProfiles();
    }
}
