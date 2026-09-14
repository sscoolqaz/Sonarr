using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.Tv;
using Sonarr.Api.V5.Episodes;
using Sonarr.Api.V5.Series;
using Sonarr.Http;
using Sonarr.Http.Extensions;

namespace Sonarr.Api.V5.History;

[V5ApiController]
public class HistoryController : Controller
{
    private readonly IHistoryService _historyService;
    private readonly ICustomFormatCalculationService _formatCalculator;
    private readonly IUpgradableSpecification _upgradableSpecification;
    private readonly IFailedDownloadService _failedDownloadService;
    private readonly ISeriesService _seriesService;
    private readonly IEpisodeService _episodeService;

    public HistoryController(IHistoryService historyService,
                         ICustomFormatCalculationService formatCalculator,
                         IUpgradableSpecification upgradableSpecification,
                         IFailedDownloadService failedDownloadService,
                         ISeriesService seriesService,
                         IEpisodeService episodeService)
    {
        _historyService = historyService;
        _formatCalculator = formatCalculator;
        _upgradableSpecification = upgradableSpecification;
        _failedDownloadService = failedDownloadService;
        _seriesService = seriesService;
        _episodeService = episodeService;
    }

    protected HistoryResource MapToResource(EpisodeHistory model, bool includeSeries, bool includeEpisode)
    {
        var resource = model.ToResource(_formatCalculator);

        if (includeSeries)
        {
            resource.Series = model.Series.ToResource();
        }

        if (includeEpisode)
        {
            resource.Episode = model.Episode.ToResource();
        }

        if (model.Series != null)
        {
            resource.QualityCutoffNotMet = _upgradableSpecification.QualityCutoffNotMet(model.Series.QualityProfile.Value, model.Quality);
        }

        return resource;
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<PagingResource<HistoryResource>>> GetHistory([FromQuery] PagingRequestResource paging, [FromQuery(Name = "eventType")] int[]? eventTypes, int? episodeId, string? downloadId, [FromQuery] int[]? seriesIds = null, [FromQuery] int[]? languages = null, [FromQuery] int[]? quality = null, [FromQuery] HistorySubresource[]? includeSubresources = null)
    {
        var pagingResource = new PagingResource<HistoryResource>(paging);
        var pagingSpec = pagingResource.MapToPagingSpec<HistoryResource, EpisodeHistory>(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "date",
                "series.sortTitle",
                "quality"
            },
            "date",
            SortDirection.Descending);

        if (eventTypes != null && eventTypes.Any())
        {
            pagingSpec.FilterExpressions.Add(v => eventTypes.Contains((int)v.EventType));
        }

        if (episodeId.HasValue)
        {
            pagingSpec.FilterExpressions.Add(h => h.EpisodeId == episodeId);
        }

        if (downloadId.IsNotNullOrWhiteSpace())
        {
            pagingSpec.FilterExpressions.Add(h => h.DownloadId == downloadId);
        }

        if (seriesIds != null && seriesIds.Any())
        {
            pagingSpec.FilterExpressions.Add(h => seriesIds.Contains(h.SeriesId));
        }

        var includeSeries = includeSubresources.Contains(HistorySubresource.Series);
        var includeEpisode = includeSubresources.Contains(HistorySubresource.Episode);

        var pagedResult = await _historyService.Paged(pagingSpec, languages, quality);

        return TypedResults.Ok(pagingSpec.ApplyToPage(h => pagedResult, h => MapToResource(h, includeSeries, includeEpisode)));
    }

    [HttpGet("since")]
    [Produces("application/json")]
    public async Task<Ok<List<HistoryResource>>> GetHistorySince(DateTime date, EpisodeHistoryEventType? eventType = null, [FromQuery] HistorySubresource[]? includeSubresources = null)
    {
        var includeSeries = includeSubresources.Contains(HistorySubresource.Series);
        var includeEpisode = includeSubresources.Contains(HistorySubresource.Episode);

        var history = await _historyService.Since(date, eventType);

        return TypedResults.Ok(history.Select(h => MapToResource(h, includeSeries, includeEpisode)).ToList());
    }

    [HttpGet("series")]
    [Produces("application/json")]
    public async Task<Ok<List<HistoryResource>>> GetSeriesHistory(int seriesId, EpisodeHistoryEventType? eventType = null, [FromQuery] HistorySubresource[]? includeSubresources = null)
    {
        var series = await _seriesService.GetSeries(seriesId);
        var includeSeries = includeSubresources.Contains(HistorySubresource.Series);
        var includeEpisode = includeSubresources.Contains(HistorySubresource.Episode);

        var history = await _historyService.GetBySeries(seriesId, eventType);

        return TypedResults.Ok(history.Select(h =>
        {
            h.Series = series;

            return MapToResource(h, includeSeries, includeEpisode);
        }).ToList());
    }

    [HttpGet("season")]
    [Produces("application/json")]
    public async Task<Ok<List<HistoryResource>>> GetSeasonHistory(int seriesId, int seasonNumber, EpisodeHistoryEventType? eventType = null, [FromQuery] HistorySubresource[]? includeSubresources = null)
    {
        var series = await _seriesService.GetSeries(seriesId);
        var includeSeries = includeSubresources.Contains(HistorySubresource.Series);
        var includeEpisode = includeSubresources.Contains(HistorySubresource.Episode);

        var history = await _historyService.GetBySeason(seriesId, seasonNumber, eventType);

        return TypedResults.Ok(history.Select(h =>
        {
            h.Series = series;

            return MapToResource(h, includeSeries, includeEpisode);
        }).ToList());
    }

    [HttpGet("episode")]
    [Produces("application/json")]
    public async Task<Ok<List<HistoryResource>>> GetEpisodeHistory(int episodeId, EpisodeHistoryEventType? eventType = null, [FromQuery] HistorySubresource[]? includeSubresources = null)
    {
        var episode = await _episodeService.GetEpisode(episodeId);
        var series = await _seriesService.GetSeries(episode.SeriesId);
        var includeSeries = includeSubresources.Contains(HistorySubresource.Series);
        var includeEpisode = includeSubresources.Contains(HistorySubresource.Episode);

        var history = await _historyService.GetByEpisode(episodeId, eventType);

        return TypedResults.Ok(history
            .Select(h =>
            {
                h.Series = series;
                h.Episode = episode;

                return MapToResource(h, includeSeries, includeEpisode);
            }).ToList());
    }

    [HttpPost("failed/{id:int}")]
    public NoContent MarkAsFailed([FromRoute] int id)
    {
        _failedDownloadService.MarkAsFailed(id);
        return TypedResults.NoContent();
    }
}
