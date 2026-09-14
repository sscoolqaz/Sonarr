using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.History
{
    public interface IHistoryRepository : IBasicRepository<EpisodeHistory>
    {
        Task<EpisodeHistory> MostRecentForEpisode(int episodeId);
        Task<List<EpisodeHistory>> FindByEpisodeId(int episodeId);
        Task<EpisodeHistory> MostRecentForDownloadId(string downloadId);
        Task<List<EpisodeHistory>> FindByDownloadId(string downloadId);
        Task<List<EpisodeHistory>> GetBySeries(int seriesId, EpisodeHistoryEventType? eventType);
        Task<List<EpisodeHistory>> GetBySeason(int seriesId, int seasonNumber, EpisodeHistoryEventType? eventType);
        Task<List<EpisodeHistory>> GetByEpisode(int episodeId, EpisodeHistoryEventType? eventType);
        Task<List<EpisodeHistory>> FindDownloadHistory(int idSeriesId, QualityModel quality);
        Task DeleteForSeries(List<int> seriesIds);
        Task<List<EpisodeHistory>> Since(DateTime date, EpisodeHistoryEventType? eventType);
        Task<PagingSpec<EpisodeHistory>> GetPaged(PagingSpec<EpisodeHistory> pagingSpec, int[] languages, int[] qualities);
    }
}
