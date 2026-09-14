using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;
using NzbDrone.SignalR;
using Sonarr.Api.V3.Episodes;
using Sonarr.Http;

namespace Sonarr.Api.V3.Calendar
{
    [V3ApiController]
    public class CalendarController : EpisodeControllerWithSignalR
    {
        private readonly ITagService _tagService;

        public CalendarController(IBroadcastSignalRMessage signalR,
                            IEpisodeService episodeService,
                            ISeriesService seriesService,
                            IUpgradableSpecification qualityUpgradableSpecification,
                            ITagService tagService,
                            ICustomFormatCalculationService formatCalculator)
            : base(episodeService, seriesService, qualityUpgradableSpecification, formatCalculator, signalR)
        {
            _tagService = tagService;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<EpisodeResource>> GetCalendar(DateTime? start, DateTime? end, bool unmonitored = false, bool includeSeries = false, bool includeEpisodeFile = false, bool includeEpisodeImages = false, string tags = "")
        {
            var startUse = start ?? DateTime.Today;
            var endUse = end ?? DateTime.Today.AddDays(2);
            var episodes = await _episodeService.EpisodesBetweenDates(startUse, endUse, unmonitored, true);
            var allSeries = await _seriesService.GetAllSeries();
            var parsedTags = new List<int>();
            var result = new List<Episode>();

            if (tags.IsNotNullOrWhiteSpace())
            {
                foreach (var tag in tags.Split(','))
                {
                    var parsedTag = await _tagService.GetTag(tag);
                    parsedTags.Add(parsedTag.Id);
                }
            }

            foreach (var episode in episodes)
            {
                var series = allSeries.SingleOrDefault(s => s.Id == episode.SeriesId);

                if (series == null)
                {
                    continue;
                }

                if (parsedTags.Any() && parsedTags.None(series.Tags.Contains))
                {
                    continue;
                }

                result.Add(episode);
            }

            var resources = await MapToResource(result, includeSeries, includeEpisodeFile, includeEpisodeImages);

            return resources.OrderBy(e => e.AirDateUtc).ToList();
        }
    }
}
