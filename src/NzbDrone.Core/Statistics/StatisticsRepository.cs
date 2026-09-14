using System.Threading.Tasks;

namespace NzbDrone.Core.Statistics;

public interface IStatisticsRepository
{
    Task<LibraryStatistics> GetLibraryStatistics(StatisticsFilter filter = null);
}
