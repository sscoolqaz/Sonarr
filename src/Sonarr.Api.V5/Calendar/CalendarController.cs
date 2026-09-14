using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Tv;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Episodes;
using Sonarr.Http;

namespace Sonarr.Api.V5.Calendar
{
    [V5ApiController]
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
        public async Task<Ok<List<EpisodeResource>>> GetCalendar(DateTime? start, DateTime? end, bool includeUnmonitored = false, bool includeSpecials = true, string tags = "", [FromQuery] CalendarSubresource[]? includeSubresources = null)
        {
            var startUse = start ?? DateTime.Today;
            var endUse = end ?? DateTime.Today.AddDays(2);
            var episodes = await _episodeService.EpisodesBetweenDates(startUse, endUse, includeUnmonitored, includeSpecials);
            var allSeries = await _seriesService.GetAllSeries();
            var parsedTags = new List<int>();
            var result = new List<Episode>();

            if (tags.IsNotNullOrWhiteSpace())
            {
                foreach (var tag in tags.Split(','))
                {
                    var resolvedTag = await _tagService.GetTag(tag);
                    parsedTags.Add(resolvedTag.Id);
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

            var includeSeries = includeSubresources.Contains(CalendarSubresource.Series);
            var includeEpisodeFile = includeSubresources.Contains(CalendarSubresource.EpisodeFile);
            var includeEpisodeImages = includeSubresources.Contains(CalendarSubresource.Images);

            var resources = await MapToResource(result, includeSeries, includeEpisodeFile, includeEpisodeImages);

            return TypedResults.Ok(resources.OrderBy(e => e.AirDateUtc).ToList());
        }
    }
}
