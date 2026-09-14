using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.SeriesStats
{
    public interface ISeriesStatisticsService
    {
        Task<List<SeriesStatistics>> SeriesStatistics();
        Task<SeriesStatistics> SeriesStatistics(int seriesId, int qualityProfileId);
    }

    public class SeriesStatisticsService : ISeriesStatisticsService
    {
        private readonly ISeriesStatisticsRepository _seriesStatisticsRepository;
        private readonly ISeriesService _seriesService;
        private readonly IQualityProfileService _qualityProfileService;

        public SeriesStatisticsService(ISeriesStatisticsRepository seriesStatisticsRepository,
                                       ISeriesService seriesService,
                                       IQualityProfileService qualityProfileService)
        {
            _seriesStatisticsRepository = seriesStatisticsRepository;
            _seriesService = seriesService;
            _qualityProfileService = qualityProfileService;
        }

        public async Task<List<SeriesStatistics>> SeriesStatistics()
        {
            var seasonStatistics = await _seriesStatisticsRepository.SeriesStatistics();
            var seriesProfiles = await _seriesService.GetAllSeriesQualityProfiles();
            var profiles = (await _qualityProfileService.All()).ToDictionary(p => p.Id);

            return seasonStatistics
                .GroupBy(s => s.SeriesId)
                .Select(s =>
                {
                    var profileId = seriesProfiles.GetValueOrDefault(s.Key);
                    profiles.TryGetValue(profileId, out var profile);
                    return MapSeriesStatistics(s.ToList(), profile);
                })
                .ToList();
        }

        public async Task<SeriesStatistics> SeriesStatistics(int seriesId, int qualityProfileId)
        {
            var stats = await _seriesStatisticsRepository.SeriesStatistics(seriesId);

            if (stats == null || stats.Count == 0)
            {
                return new SeriesStatistics();
            }

            var profile = await _qualityProfileService.Get(qualityProfileId);

            return MapSeriesStatistics(stats, profile);
        }

        private SeriesStatistics MapSeriesStatistics(List<SeasonStatistics> seasonStatistics, QualityProfile profile)
        {
            var seriesStatistics = new SeriesStatistics
            {
                SeasonStatistics = seasonStatistics,
                SeriesId = seasonStatistics.First().SeriesId,
                EpisodeFileCount = seasonStatistics.Sum(s => s.EpisodeFileCount),
                EpisodeCount = seasonStatistics.Sum(s => s.EpisodeCount),
                TotalEpisodeCount = seasonStatistics.Sum(s => s.TotalEpisodeCount),
                MonitoredEpisodeCount = seasonStatistics.Sum(s => s.MonitoredEpisodeCount),
                SizeOnDisk = seasonStatistics.Sum(s => s.SizeOnDisk),
                ReleaseGroups = seasonStatistics.SelectMany(s => s.ReleaseGroups).Distinct().ToList(),
                ReleaseTypes = seasonStatistics.SelectMany(s => s.ReleaseTypes).Distinct().OrderBy(s => s).ToList(),
                EpisodeFileQualities = SortQualities(seasonStatistics.SelectMany(s => s.EpisodeFileQualities).Distinct().ToList(), profile)
            };

            var nextAiring = seasonStatistics.Where(s => s.NextAiring != null).MinBy(s => s.NextAiring);
            var previousAiring = seasonStatistics.Where(s => s.PreviousAiring != null).MaxBy(s => s.PreviousAiring);
            var lastAired = seasonStatistics.Where(s => s.SeasonNumber > 0 && s.LastAired != null).MaxBy(s => s.LastAired);

            seriesStatistics.NextAiring = nextAiring?.NextAiring;
            seriesStatistics.PreviousAiring = previousAiring?.PreviousAiring;
            seriesStatistics.LastAired = lastAired?.LastAired;

            return seriesStatistics;
        }

        private static List<Quality> SortQualities(List<Quality> qualities, QualityProfile profile)
        {
            if (profile == null)
            {
                return qualities;
            }

            return qualities.OrderBy(q => profile.GetIndex(q.Id).Index).ToList();
        }
    }
}
