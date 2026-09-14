using System.Collections.Concurrent;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.SeriesStats;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Series;

[V5ApiController]
public class SeriesController : RestControllerWithSignalR<SeriesResource, NzbDrone.Core.Tv.Series>,
                            IHandle<EpisodeImportedEvent>,
                            IHandle<EpisodeFileDeletedEvent>,
                            IHandle<SeriesUpdatedEvent>,
                            IHandle<SeriesEditedEvent>,
                            IHandle<SeriesDeletedEvent>,
                            IHandle<SeriesRenamedEvent>,
                            IHandle<SeriesBulkEditedEvent>,
                            IHandle<MediaCoversUpdatedEvent>
{
    private readonly ISeriesService _seriesService;
    private readonly IAddSeriesService _addSeriesService;
    private readonly ISeriesStatisticsService _seriesStatisticsService;
    private readonly ISceneMappingService _sceneMappingService;
    private readonly IMapCoversToLocal _coverMapper;
    private readonly IManageCommandQueue _commandQueueManager;
    private readonly IRootFolderService _rootFolderService;

    // NOTE: the original synchronous `lock` (via LockByIdPool) can't wrap an `await` (CS1996),
    // and the series lookup/update calls below are genuinely async now, so a per-series
    // SemaphoreSlim replaces the lock to serialize season-monitored updates without blocking the
    // request thread (see Release/ReleasePushController.cs for the same pattern).
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _seriesLocks = new();

    public SeriesController(IBroadcastSignalRMessage signalRBroadcaster,
                        ISeriesService seriesService,
                        IAddSeriesService addSeriesService,
                        ISeriesStatisticsService seriesStatisticsService,
                        ISceneMappingService sceneMappingService,
                        IMapCoversToLocal coverMapper,
                        IManageCommandQueue commandQueueManager,
                        IRootFolderService rootFolderService,
                        RootFolderValidator rootFolderValidator,
                        MappedNetworkDriveValidator mappedNetworkDriveValidator,
                        SeriesPathValidator seriesPathValidator,
                        SeriesExistsValidator seriesExistsValidator,
                        SeriesAncestorValidator seriesAncestorValidator,
                        SystemFolderValidator systemFolderValidator,
                        QualityProfileExistsValidator qualityProfileExistsValidator,
                        RootFolderExistsValidator rootFolderExistsValidator,
                        SeriesFolderAsRootFolderValidator seriesFolderAsRootFolderValidator)
        : base(signalRBroadcaster)
    {
        _seriesService = seriesService;
        _addSeriesService = addSeriesService;
        _seriesStatisticsService = seriesStatisticsService;
        _sceneMappingService = sceneMappingService;

        _coverMapper = coverMapper;
        _commandQueueManager = commandQueueManager;
        _rootFolderService = rootFolderService;

        SharedValidator.RuleFor(s => s.Path).Cascade(CascadeMode.Stop)
            .IsValidPath()
            .SetValidator(rootFolderValidator)
            .SetValidator(mappedNetworkDriveValidator)
            .SetValidator(seriesPathValidator)
            .SetValidator(seriesAncestorValidator)
            .SetValidator(systemFolderValidator)
            .When(s => s.Path.IsNotNullOrWhiteSpace());

        PostValidator.RuleFor(s => s.Path).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .IsValidPath()
            .When(s => s.RootFolderPath.IsNullOrWhiteSpace());
        PostValidator.RuleFor(s => s.RootFolderPath).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .IsValidPath()
            .SetValidator(rootFolderExistsValidator)
            .SetValidator(seriesFolderAsRootFolderValidator)
            .When(s => s.Path.IsNullOrWhiteSpace());

        PutValidator.RuleFor(s => s.Path).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .IsValidPath();

        SharedValidator.RuleFor(s => s.QualityProfileId).Cascade(CascadeMode.Stop)
            .ValidId()
            .SetValidator(qualityProfileExistsValidator);

        PostValidator.RuleFor(s => s.Title).NotEmpty();
        PostValidator.RuleFor(s => s.TvdbId).GreaterThan(0).SetValidator(seriesExistsValidator);
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<SeriesResource>>> AllSeries(int? tvdbId, [FromQuery] SeriesSubresource[]? includeSubresources = null)
    {
        var seriesStats = await _seriesStatisticsService.SeriesStatistics();
        var seriesResources = new List<SeriesResource>();
        var includeSeasonImages = includeSubresources.Contains(SeriesSubresource.SeasonImages);

        if (tvdbId.HasValue)
        {
            var series = await _seriesService.FindByTvdbId(tvdbId.Value);
            seriesResources.AddIfNotNull(series?.ToResource(includeSeasonImages));
        }
        else
        {
            var allSeries = await _seriesService.GetAllSeries();
            seriesResources.AddRange(allSeries.Select(s => s.ToResource(includeSeasonImages)));
        }

        MapCoversToLocal(seriesResources.ToArray());
        LinkSeriesStatistics(seriesResources, seriesStats.ToDictionary(x => x.SeriesId));
        await PopulateAlternateTitles(seriesResources);

        foreach (var resource in seriesResources)
        {
            await LinkRootFolderPath(resource);
        }

        return TypedResults.Ok(seriesResources);
    }

    [NonAction]
    public override Results<Ok<SeriesResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
    {
        return base.GetResourceByIdWithErrorHandler(id);
    }

    [RestGetById]
    [Produces("application/json")]
    public async Task<Results<Ok<SeriesResource>, NotFound>> GetResourceByIdWithErrorHandler(int id, [FromQuery] SeriesSubresource[]? includeSubresources = null)
    {
        var includeSeasonImages = includeSubresources.Contains(SeriesSubresource.SeasonImages);

        try
        {
            var series = await GetSeriesResourceById(id, includeSeasonImages);

            return series == null ? TypedResults.NotFound() : TypedResults.Ok(series);
        }
        catch (ModelNotFoundException)
        {
            return TypedResults.NotFound();
        }
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override SeriesResource? GetResourceById(int id)
    {
        var includeSubresources = Request?.Query["includeSubresources"].Select(v =>
        {
            if (Enum.TryParse<SeriesSubresource>(v, true, out var enumValue))
            {
                return enumValue;
            }

            throw new BadRequestException($"The value '{v}' is not valid.");
        }) ?? [];

        var includeSeasonImages = includeSubresources.Contains(SeriesSubresource.SeasonImages);

        return GetSeriesResourceById(id, includeSeasonImages).GetAwaiter().GetResult();
    }

    private async Task<SeriesResource?> GetSeriesResourceById(int id, bool includeSeasonImages)
    {
        var series = await _seriesService.GetSeries(id);

        return await GetSeriesResource(series, includeSeasonImages);
    }

    [RestPostById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<Results<Created<SeriesResource>, NotFound>> AddSeries([FromBody] SeriesResource seriesResource)
    {
        var series = await _addSeriesService.AddSeries(seriesResource.ToModel());

        return TypedCreated(series.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<Results<Accepted<SeriesResource>, NotFound>> UpdateSeries([FromBody] SeriesResource seriesResource, [FromQuery] bool moveFiles = false)
    {
        var series = await _seriesService.GetSeries(seriesResource.Id);

        if (moveFiles)
        {
            var sourcePath = series.Path;
            var destinationPath = seriesResource.Path;

            await _commandQueueManager.Push(new MoveSeriesCommand
            {
                SeriesId = series.Id,
                SourcePath = sourcePath,
                DestinationPath = destinationPath
            },
                trigger: CommandTrigger.Manual);
        }

        var model = seriesResource.ToModel(series);

        await _seriesService.UpdateSeries(model);

        BroadcastResourceChange(ModelAction.Updated, seriesResource);

        return TypedAccepted(seriesResource.Id);
    }

    [HttpPut("{id}/season")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<Results<Ok<SeasonResource>, NotFound>> UpdateSeasonMonitored([FromRoute] int id, [FromBody] SeasonResource seasonResource)
    {
        var seriesLock = _seriesLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await seriesLock.WaitAsync();

        try
        {
            var series = await _seriesService.GetSeries(id);
            var season = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonResource.SeasonNumber);

            if (season == null)
            {
                return TypedResults.NotFound();
            }

            season.Monitored = seasonResource.Monitored;

            await _seriesService.UpdateSeries(series);

            BroadcastResourceChange(ModelAction.Updated, (await GetSeriesResource(series, false))!);

            return TypedResults.Ok(season.ToResource());
        }
        finally
        {
            seriesLock.Release();
        }
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteSeries(int id, bool deleteFiles = false, bool addImportListExclusion = false)
    {
        await _seriesService.DeleteSeries(new List<int> { id }, deleteFiles, addImportListExclusion);

        return TypedResults.NoContent();
    }

    private async Task<SeriesResource?> GetSeriesResource(NzbDrone.Core.Tv.Series? series, bool includeSeasonImages)
    {
        if (series == null)
        {
            return null;
        }

        var resource = series.ToResource(includeSeasonImages);
        MapCoversToLocal(resource);
        await FetchAndLinkSeriesStatistics(resource);
        await PopulateAlternateTitles(resource);
        await LinkRootFolderPath(resource);

        return resource;
    }

    private void MapCoversToLocal(params SeriesResource[] series)
    {
        foreach (var seriesResource in series)
        {
            _coverMapper.ConvertToLocalUrls(seriesResource.Id, seriesResource.Images);
        }
    }

    private async Task FetchAndLinkSeriesStatistics(SeriesResource resource)
    {
        LinkSeriesStatistics(resource, await _seriesStatisticsService.SeriesStatistics(resource.Id, resource.QualityProfileId));
    }

    private void LinkSeriesStatistics(List<SeriesResource> resources, Dictionary<int, SeriesStatistics> seriesStatistics)
    {
        foreach (var series in resources)
        {
            if (seriesStatistics.TryGetValue(series.Id, out var stats))
            {
                LinkSeriesStatistics(series, stats);
            }
        }
    }

    private void LinkSeriesStatistics(SeriesResource resource, SeriesStatistics seriesStatistics)
    {
        // Only set last aired from statistics if it's missing from the series itself
        resource.LastAired ??= seriesStatistics.LastAired;

        resource.PreviousAiring = seriesStatistics.PreviousAiring;
        resource.NextAiring = seriesStatistics.NextAiring;
        resource.Statistics = seriesStatistics.ToResource(resource.Seasons);

        if (seriesStatistics.SeasonStatistics != null)
        {
            foreach (var season in resource.Seasons)
            {
                season.Statistics = seriesStatistics.SeasonStatistics?.SingleOrDefault(s => s.SeasonNumber == season.SeasonNumber)?.ToResource();
            }
        }
    }

    private async Task PopulateAlternateTitles(List<SeriesResource> resources)
    {
        foreach (var resource in resources)
        {
            await PopulateAlternateTitles(resource);
        }
    }

    private async Task PopulateAlternateTitles(SeriesResource resource)
    {
        var mappings = await _sceneMappingService.FindByTvdbId(resource.TvdbId);

        if (mappings == null)
        {
            return;
        }

        resource.AlternateTitles = mappings.ConvertAll(AlternateTitleResourceMapper.ToResource);
    }

    private async Task LinkRootFolderPath(SeriesResource resource)
    {
        resource.RootFolderPath = await _rootFolderService.GetBestRootFolderPath(resource.Path);
    }

    [NonAction]
    public void Handle(EpisodeImportedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.ImportedEpisode.SeriesId);
    }

    [NonAction]
    public void Handle(EpisodeFileDeletedEvent message)
    {
        if (message.Reason == DeleteMediaFileReason.Upgrade)
        {
            return;
        }

        BroadcastResourceChange(ModelAction.Updated, message.EpisodeFile.SeriesId);
    }

    [NonAction]
    public void Handle(SeriesUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.Series.Id);
    }

    // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide) with a
    // synchronous void Handle(...) signature; converting it is out of scope for this pass, so we
    // bridge to the now-async GetSeriesResource here as the documented boundary.
    [NonAction]
    public void Handle(SeriesEditedEvent message)
    {
        var resource = GetSeriesResource(message.Series, false).GetAwaiter().GetResult();

        if (resource == null)
        {
            return;
        }

        resource.EpisodesChanged = message.EpisodesChanged;
        BroadcastResourceChange(ModelAction.Updated, resource);
    }

    [NonAction]
    public void Handle(SeriesDeletedEvent message)
    {
        foreach (var series in message.Series)
        {
            var resource = GetSeriesResource(series, false).GetAwaiter().GetResult();

            if (resource == null)
            {
                continue;
            }

            BroadcastResourceChange(ModelAction.Deleted, resource);
        }
    }

    [NonAction]
    public void Handle(SeriesRenamedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.Series.Id);
    }

    [NonAction]
    public void Handle(SeriesBulkEditedEvent message)
    {
        foreach (var series in message.Series)
        {
            var resource = GetSeriesResource(series, false).GetAwaiter().GetResult();

            if (resource == null)
            {
                continue;
            }

            BroadcastResourceChange(ModelAction.Updated, resource);
        }
    }

    [NonAction]
    public void Handle(MediaCoversUpdatedEvent message)
    {
        if (message.Updated)
        {
            BroadcastResourceChange(ModelAction.Updated, message.Series.Id);
        }
    }
}
