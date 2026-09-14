using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
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

namespace Sonarr.Api.V3.EpisodeFiles
{
    [V3ApiController]
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
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override EpisodeFileResource GetResourceById(int id)
        {
            var episodeFile = _mediaFileService.Get(id).GetAwaiter().GetResult();
            var series = _seriesService.GetSeries(episodeFile.SeriesId).GetAwaiter().GetResult();

            var resource = episodeFile.ToResource(series, _upgradableSpecification, _formatCalculator);

            return resource;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<EpisodeFileResource>> GetEpisodeFiles(int? seriesId, [FromQuery] List<int> episodeFileIds)
        {
            if (!seriesId.HasValue && !episodeFileIds.Any())
            {
                throw new BadRequestException("seriesId or episodeFileIds must be provided");
            }

            if (seriesId.HasValue)
            {
                var series = await _seriesService.GetSeries(seriesId.Value);
                var files = await _mediaFileService.GetFilesBySeries(seriesId.Value);

                if (files == null)
                {
                    return new List<EpisodeFileResource>();
                }

                return files.ConvertAll(e => e.ToResource(series, _upgradableSpecification, _formatCalculator))
                            .ToList();
            }
            else
            {
                var episodeFiles = await _mediaFileService.Get(episodeFileIds);
                var seriesById = new Dictionary<int, NzbDrone.Core.Tv.Series>();

                var result = new List<EpisodeFileResource>();

                foreach (var group in episodeFiles.GroupBy(e => e.SeriesId))
                {
                    if (!seriesById.TryGetValue(group.Key, out var series))
                    {
                        series = await _seriesService.GetSeries(group.Key);
                        seriesById[group.Key] = series;
                    }

                    result.AddRange(group.ToList().ConvertAll(e => e.ToResource(series, _upgradableSpecification, _formatCalculator)));
                }

                return result;
            }
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<EpisodeFileResource>> SetQuality([FromBody] EpisodeFileResource episodeFileResource)
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
            return Accepted(episodeFile.Id);
        }

        [Obsolete("Use bulk endpoint instead")]
        [HttpPut("editor")]
        [Consumes("application/json")]
        public async Task<object> SetQuality([FromBody] EpisodeFileListResource resource)
        {
            var episodeFiles = await _mediaFileService.GetFiles(resource.EpisodeFileIds);

            foreach (var episodeFile in episodeFiles)
            {
                if (resource.Languages != null)
                {
                    episodeFile.Languages = resource.Languages;
                }

                if (resource.Quality != null)
                {
                    episodeFile.Quality = resource.Quality;
                }

                if (resource.SceneName != null && SceneChecker.IsSceneTitle(resource.SceneName))
                {
                    episodeFile.SceneName = resource.SceneName;
                }

                if (resource.ReleaseGroup != null)
                {
                    episodeFile.ReleaseGroup = resource.ReleaseGroup;
                }
            }

            await _mediaFileService.Update(episodeFiles);

            var series = await _seriesService.GetSeries(episodeFiles.First().SeriesId);

            return Accepted(episodeFiles.ConvertAll(f => f.ToResource(series, _upgradableSpecification, _formatCalculator)));
        }

        [RestDeleteById]
        public async Task DeleteEpisodeFile(int id)
        {
            var episodeFile = await _mediaFileService.Get(id);

            if (episodeFile == null)
            {
                throw new NzbDroneClientException(HttpStatusCode.NotFound, "Episode file not found");
            }

            var series = await _seriesService.GetSeries(episodeFile.SeriesId);

            await _mediaFileDeletionService.DeleteEpisodeFile(series, episodeFile);
        }

        [HttpDelete("bulk")]
        [Consumes("application/json")]
        public async Task<object> DeleteEpisodeFiles([FromBody] EpisodeFileListResource resource)
        {
            var episodeFiles = await _mediaFileService.GetFiles(resource.EpisodeFileIds);
            var series = await _seriesService.GetSeries(episodeFiles.First().SeriesId);

            foreach (var episodeFile in episodeFiles)
            {
                await _mediaFileDeletionService.DeleteEpisodeFile(series, episodeFile);
            }

            return new { };
        }

        [HttpPut("bulk")]
        [Consumes("application/json")]
        public async Task<object> SetPropertiesBulk([FromBody] List<EpisodeFileResource> resources)
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

            return Accepted(episodeFiles.ConvertAll(f => f.ToResource(series, _upgradableSpecification, _formatCalculator)));
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
}
