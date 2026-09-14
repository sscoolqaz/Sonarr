using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Tv;
using NzbDrone.SignalR;
using Sonarr.Api.V3.Episodes;
using Sonarr.Http;

namespace Sonarr.Api.V3.Wanted
{
    [V3ApiController("wanted/cutoff")]
    public class CutoffController : EpisodeControllerWithSignalR
    {
        private readonly IEpisodeCutoffService _episodeCutoffService;

        public CutoffController(IEpisodeCutoffService episodeCutoffService,
                            IEpisodeService episodeService,
                            ISeriesService seriesService,
                            IUpgradableSpecification upgradableSpecification,
                            ICustomFormatCalculationService formatCalculator,
                            IBroadcastSignalRMessage signalRBroadcaster)
            : base(episodeService, seriesService, upgradableSpecification, formatCalculator, signalRBroadcaster)
        {
            _episodeCutoffService = episodeCutoffService;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<PagingResource<EpisodeResource>> GetCutoffUnmetEpisodes([FromQuery] PagingRequestResource paging, bool includeSeries = false, bool includeEpisodeFile = false, bool includeImages = false, bool monitored = true)
        {
            var pagingResource = new PagingResource<EpisodeResource>(paging);
            var pagingSpec = pagingResource.MapToPagingSpec<EpisodeResource, Episode>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "episodes.airDateUtc",
                    "episodes.lastSearchTime",
                    "series.sortTitle"
                },
                "episodes.airDateUtc",
                SortDirection.Ascending);

            if (monitored)
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == true && v.Series.Monitored == true);
            }
            else
            {
                pagingSpec.FilterExpressions.Add(v => v.Monitored == false || v.Series.Monitored == false);
            }

            var pagedSpec = _episodeCutoffService.EpisodesWhereCutoffUnmet(pagingSpec);
            var mappedRecords = await MapToResource(pagedSpec.Records, includeSeries, includeEpisodeFile, includeImages);

            return new PagingResource<EpisodeResource>
            {
                Page = pagedSpec.Page,
                PageSize = pagedSpec.PageSize,
                SortDirection = pagedSpec.SortDirection,
                SortKey = pagedSpec.SortKey,
                TotalRecords = pagedSpec.TotalRecords,
                Records = mappedRecords
            };
        }
    }
}
