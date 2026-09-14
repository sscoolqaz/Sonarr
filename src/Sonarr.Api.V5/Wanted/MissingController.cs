using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Tv;
using NzbDrone.SignalR;
using Sonarr.Api.V5.Episodes;
using Sonarr.Http;

namespace Sonarr.Api.V5.Wanted;

[V5ApiController("wanted/missing")]
public class MissingController : EpisodeControllerWithSignalR
{
    public MissingController(IEpisodeService episodeService,
                         ISeriesService seriesService,
                         IUpgradableSpecification upgradableSpecification,
                         ICustomFormatCalculationService formatCalculator,
                         IBroadcastSignalRMessage signalRBroadcaster)
        : base(episodeService, seriesService, upgradableSpecification, formatCalculator, signalRBroadcaster)
    {
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<PagingResource<EpisodeResource>>> GetMissingEpisodes(
        [FromQuery] PagingRequestResource paging,
        [FromQuery] bool monitored = true,
        [FromQuery] bool includeSpecials = true,
        [FromQuery] List<int>? seriesIds = null,
        [FromQuery] List<int>? qualityProfileIds = null,
        [FromQuery] List<SeriesTypes>? seriesType = null,
        [FromQuery] HashSet<int>? seriesTags = null,
        [FromQuery] MissingSubresource[]? includeSubresources = null)
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

        if (seriesIds?.Any() == true)
        {
            pagingSpec.FilterExpressions.Add(e => seriesIds.Contains(e.SeriesId));
        }

        if (qualityProfileIds?.Any() == true)
        {
            pagingSpec.FilterExpressions.Add(e => qualityProfileIds.Contains(e.Series.QualityProfileId));
        }

        if (seriesType?.Any() == true)
        {
            pagingSpec.FilterExpressions.Add(e => seriesType.Contains(e.Series.SeriesType));
        }

        var includeSeries = includeSubresources.Contains(MissingSubresource.Series);
        var includeImages = includeSubresources.Contains(MissingSubresource.Images);

        var pagedResult = await _episodeService.EpisodesWithoutFiles(pagingSpec, includeSpecials, seriesTags);

        // NOTE: Sonarr.Http.Extensions.RequestExtensions.ApplyToPage takes a synchronous
        // Converter<TModel, TResource>, but mapping an Episode to its resource now requires
        // awaiting the series lookup (see EpisodeControllerWithSignalR.MapToResource). We build
        // the PagingResource by hand here instead of bridging to sync inside the mapper.
        var mappedRecords = await MapToResource(pagedResult.Records, includeSeries, false, includeImages);

        var resource = new PagingResource<EpisodeResource>
        {
            Page = pagedResult.Page,
            PageSize = pagedResult.PageSize,
            SortDirection = pagedResult.SortDirection,
            SortKey = pagedResult.SortKey,
            TotalRecords = pagedResult.TotalRecords,
            Records = mappedRecords
        };

        return TypedResults.Ok(resource);
    }
}
