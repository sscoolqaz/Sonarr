using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Tv;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Episodes
{
    [V3ApiController]
    public class EpisodeController : EpisodeControllerWithSignalR
    {
        public EpisodeController(ISeriesService seriesService,
                             IEpisodeService episodeService,
                             IUpgradableSpecification upgradableSpecification,
                             ICustomFormatCalculationService formatCalculator,
                             IBroadcastSignalRMessage signalRBroadcaster)
            : base(episodeService, seriesService, upgradableSpecification, formatCalculator, signalRBroadcaster)
        {
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<EpisodeResource>> GetEpisodes(int? seriesId, int? seasonNumber, [FromQuery]List<int> episodeIds, int? episodeFileId, bool includeSeries = false, bool includeEpisodeFile = false, bool includeImages = false)
        {
            if (seriesId.HasValue)
            {
                if (seasonNumber.HasValue)
                {
                    return await MapToResource(await _episodeService.GetEpisodesBySeason(seriesId.Value, seasonNumber.Value), includeSeries, includeEpisodeFile, includeImages);
                }

                return await MapToResource(await _episodeService.GetEpisodeBySeries(seriesId.Value), includeSeries, includeEpisodeFile, includeImages);
            }
            else if (episodeIds.Any())
            {
                return await MapToResource(await _episodeService.GetEpisodes(episodeIds), includeSeries, includeEpisodeFile, includeImages);
            }
            else if (episodeFileId.HasValue)
            {
                return await MapToResource(await _episodeService.GetEpisodesByFileId(episodeFileId.Value), includeSeries, includeEpisodeFile, includeImages);
            }

            throw new BadRequestException("seriesId or episodeIds must be provided");
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<EpisodeResource>> SetEpisodeMonitored([FromRoute] int id, [FromBody] EpisodeResource resource)
        {
            await _episodeService.SetEpisodeMonitored(id, resource.Monitored);

            resource = await MapToResource(await _episodeService.GetEpisode(id), false, false, false);

            return Accepted(resource);
        }

        [HttpPut("monitor")]
        [Consumes("application/json")]
        public async Task<IActionResult> SetEpisodesMonitored([FromBody] EpisodesMonitoredResource resource, [FromQuery] bool includeImages = false)
        {
            if (resource.EpisodeIds.Count == 1)
            {
                await _episodeService.SetEpisodeMonitored(resource.EpisodeIds.First(), resource.Monitored);
            }
            else
            {
                await _episodeService.SetMonitored(resource.EpisodeIds, resource.Monitored);
            }

            var resources = await MapToResource(await _episodeService.GetEpisodes(resource.EpisodeIds), false, false, includeImages);

            return Accepted(resources);
        }
    }
}
