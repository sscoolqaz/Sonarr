using System.Threading.Tasks;

namespace NzbDrone.Core.Statistics;

public interface IStatisticsService
{
    Task<LibraryStatistics> GetLibraryStatistics(StatisticsFilter filter = null);
}

public class StatisticsService : IStatisticsService
{
    private readonly IStatisticsRepository _statisticsRepository;

    public StatisticsService(IStatisticsRepository statisticsRepository)
    {
        _statisticsRepository = statisticsRepository;
    }

    public async Task<LibraryStatistics> GetLibraryStatistics(StatisticsFilter filter = null)
    {
        return await _statisticsRepository.GetLibraryStatistics(filter);
    }
}
