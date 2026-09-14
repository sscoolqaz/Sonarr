using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;
using BadRequestException = Sonarr.Http.REST.BadRequestException;

namespace Sonarr.Api.V5.EpisodeFiles;

[V5ApiController]
public class EpisodeFileController : RestControllerWithSignalR<EpisodeFileResource, EpisodeFile>,
                             IHandle<EpisodeFileAddedEvent>,
                             IHandle<EpisodeFileDeletedEvent>
{
    private readonly IMediaFileService _mediaFileService;
    private readonly IDeleteMediaFiles _mediaFileDeletionService;
    private readonly ISeriesService _seriesService;
    private readonly ICustomFormatCalculationService _formatCalculator;
    private readonly IUpgradableSpecification _upgradableSpecification;

    public EpisodeFileController(IBroadcastSignalRMessage signalRBroadcaster,
                         IMediaFileService mediaFileService,
                         IDeleteMediaFiles mediaFileDeletionService,
                         ISeriesService seriesService,
                         ICustomFormatCalculationService formatCalculator,
                         IUpgradableSpecification upgradableSpecification)
        : base(signalRBroadcaster)
    {
        _mediaFileService = mediaFileService;
        _mediaFileDeletionService = mediaFileDeletionService;
        _seriesService = seriesService;
        _formatCalculator = formatCalculator;
        _upgradableSpecification = upgradableSpecification;
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override EpisodeFileResource GetResourceById(int id)
    {
        var episodeFile = _mediaFileService.Get(id).GetAwaiter().GetResult();
        var series = _seriesService.GetSeries(episodeFile.SeriesId).GetAwaiter().GetResult();

        var resource = episodeFile.ToResource(series, _upgradableSpecification, _formatCalculator);

        return resource;
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Results<Ok<List<EpisodeFileResource>>, BadRequest>> GetEpisodeFiles(int? seriesId, [FromQuery] List<int>? episodeFileIds)
    {
        if (!seriesId.HasValue && episodeFileIds?.Any() == false)
        {
            throw new BadRequestException("seriesId or episodeFileIds must be provided");
        }

        if (seriesId.HasValue)
        {
            var series = await _seriesService.GetSeries(seriesId.Value);
            var files = await _mediaFileService.GetFilesBySeries(seriesId.Value);

            if (files == null)
            {
                return TypedResults.Ok(new List<EpisodeFileResource>());
            }

            return TypedResults.Ok(files.ConvertAll(e => e.ToResource(series, _upgradableSpecification, _formatCalculator)));
        }
        else
        {
            var episodeFiles = await _mediaFileService.Get(episodeFileIds);
            var result = new List<EpisodeFileResource>();

            foreach (var group in episodeFiles.GroupBy(e => e.SeriesId))
            {
                var series = await _seriesService.GetSeries(group.Key);
                result.AddRange(group.ToList().ConvertAll(e => e.ToResource(series, _upgradableSpecification, _formatCalculator)));
            }

            return TypedResults.Ok(result);
        }
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<EpisodeFileResource>, NotFound>> SetQuality([FromBody] EpisodeFileResource episodeFileResource)
    {
        var episodeFile = await _mediaFileService.Get(episodeFileResource.Id);
        episodeFile.Quality = episodeFileResource.Quality;

        if (episodeFileResource.SceneName != null && SceneChecker.IsSceneTitle(episodeFileResource.SceneName))
        {
            episodeFile.SceneName = episodeFileResource.SceneName;
        }

        if (episodeFileResource.ReleaseGroup != null)
        {
            episodeFile.ReleaseGroup = episodeFileResource.ReleaseGroup;
        }

        await _mediaFileService.Update(episodeFile);
        return TypedAccepted(episodeFile.Id);
    }

    [RestDeleteById]
    public async Task<Results<NoContent, NotFound>> DeleteEpisodeFile(int id)
    {
        var episodeFile = await _mediaFileService.Get(id);

        if (episodeFile == null)
        {
            throw new NzbDroneClientException(HttpStatusCode.NotFound, "Episode file not found");
        }

        var series = await _seriesService.GetSeries(episodeFile.SeriesId);

        await _mediaFileDeletionService.DeleteEpisodeFile(series, episodeFile);

        return TypedResults.NoContent();
    }

    [HttpDelete("bulk")]
    [Consumes("application/json")]
    public async Task<NoContent> DeleteEpisodeFiles([FromBody] EpisodeFileListResource resource)
    {
        var episodeFiles = await _mediaFileService.GetFiles(resource.EpisodeFileIds);
        var series = await _seriesService.GetSeries(episodeFiles.First().SeriesId);

        foreach (var episodeFile in episodeFiles)
        {
            await _mediaFileDeletionService.DeleteEpisodeFile(series, episodeFile);
        }

        return TypedResults.NoContent();
    }

    [HttpPut("bulk")]
    [Consumes("application/json")]
    public async Task<Ok<List<EpisodeFileResource>>> SetPropertiesBulk([FromBody] List<EpisodeFileResource> resources)
    {
        var episodeFiles = await _mediaFileService.GetFiles(resources.Select(r => r.Id));

        foreach (var episodeFile in episodeFiles)
        {
            var resourceEpisodeFile = resources.Single(r => r.Id == episodeFile.Id);

            if (resourceEpisodeFile.Languages != null)
            {
                // Don't allow user to set files with 'Original' language
                episodeFile.Languages = resourceEpisodeFile.Languages.Where(l => l != null && l != Language.Original).ToList();
            }

            if (resourceEpisodeFile.Quality != null)
            {
                episodeFile.Quality = resourceEpisodeFile.Quality;
            }

            if (resourceEpisodeFile.SceneName != null && SceneChecker.IsSceneTitle(resourceEpisodeFile.SceneName))
            {
                episodeFile.SceneName = resourceEpisodeFile.SceneName;
            }

            if (resourceEpisodeFile.ReleaseGroup != null)
            {
                episodeFile.ReleaseGroup = resourceEpisodeFile.ReleaseGroup;
            }

            if (resourceEpisodeFile.IndexerFlags.HasValue)
            {
                episodeFile.IndexerFlags = (IndexerFlags)resourceEpisodeFile.IndexerFlags;
            }

            if (resourceEpisodeFile.ReleaseType != null)
            {
                episodeFile.ReleaseType = (ReleaseType)resourceEpisodeFile.ReleaseType;
            }
        }

        await _mediaFileService.Update(episodeFiles);

        var series = await _seriesService.GetSeries(episodeFiles.First().SeriesId);

        return TypedResults.Ok(episodeFiles.ConvertAll(f => f.ToResource(series, _upgradableSpecification, _formatCalculator)));
    }

    [NonAction]
    public void Handle(EpisodeFileAddedEvent message)
    {
        BroadcastResourceChange(ModelAction.Updated, message.EpisodeFile.Id);
    }

    [NonAction]
    public void Handle(EpisodeFileDeletedEvent message)
    {
        BroadcastResourceChange(ModelAction.Deleted, message.EpisodeFile.Id);
    }
}
