using System.Collections.Generic;
using System.Threading.Tasks;

namespace NzbDrone.Core.SeriesStats
{
    public interface ISeriesStatisticsRepository
    {
        Task<List<SeasonStatistics>> SeriesStatistics();
        Task<List<SeasonStatistics>> SeriesStatistics(int seriesId);
    }
}
