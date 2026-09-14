using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Tv
{
    public interface IEpisodeRepository : IBasicRepository<Episode>
    {
        Task<Episode> Find(int seriesId, int season, int episodeNumber);
        Task<Episode> Find(int seriesId, int absoluteEpisodeNumber);
        Task<List<Episode>> Find(int seriesId, string date);
        Task<List<Episode>> GetEpisodes(int seriesId);
        Task<List<Episode>> GetEpisodes(int seriesId, int seasonNumber);
        Task<List<Episode>> GetEpisodesBySeriesIds(List<int> seriesIds);
        Task<List<Episode>> GetEpisodesBySceneSeason(int seriesId, int sceneSeasonNumber);
        Task<List<Episode>> GetEpisodeByFileId(int fileId);
        Task<List<Episode>> EpisodesWithFiles(int seriesId);
        Task<PagingSpec<Episode>> EpisodesWithoutFiles(PagingSpec<Episode> pagingSpec, bool includeSpecials, HashSet<int> seriesTags = null);
        Task<PagingSpec<Episode>> EpisodesWhereCutoffUnmet(PagingSpec<Episode> pagingSpec, List<QualitiesBelowCutoff> qualitiesBelowCutoff, bool includeSpecials, HashSet<int> seriesTags = null, List<int> quality = null);
        Task<List<Episode>> FindEpisodesBySceneNumbering(int seriesId, int seasonNumber, int episodeNumber);
        Task<List<Episode>> FindEpisodesBySceneNumbering(int seriesId, int sceneAbsoluteEpisodeNumber);
        Task<List<Episode>> EpisodesBetweenDates(DateTime startDate, DateTime endDate, bool includeUnmonitored, bool includeSpecials);
        Task SetMonitoredFlat(Episode episode, bool monitored);
        Task SetMonitoredBySeason(int seriesId, int seasonNumber, bool monitored);
        Task SetMonitored(IEnumerable<int> ids, bool monitored);
        Task<List<int>> SetMonitored(int seriesId, MonitorTypes monitor, int firstSeason, int lastSeason);
        Task SetFileId(Episode episode, int fileId);
        Task ClearFileId(Episode episode, bool unmonitor);
    }
}
