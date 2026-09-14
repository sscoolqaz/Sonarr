namespace NzbDrone.Core.Statistics;

public interface IStatisticsRepository
{
    LibraryStatistics GetLibraryStatistics(StatisticsFilter filter = null);
}
