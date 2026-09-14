using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Commands;
using Sonarr.Http;

namespace Sonarr.Api.V3.Series
{
    [V3ApiController("series/editor")]
    public class SeriesEditorController : Controller
    {
        private readonly ISeriesService _seriesService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly SeriesEditorValidator _seriesEditorValidator;

        public SeriesEditorController(ISeriesService seriesService, IManageCommandQueue commandQueueManager, SeriesEditorValidator seriesEditorValidator)
        {
            _seriesService = seriesService;
            _commandQueueManager = commandQueueManager;
            _seriesEditorValidator = seriesEditorValidator;
        }

        [HttpPut]
        public async Task<object> SaveAll([FromBody] SeriesEditorResource resource)
        {
            var seriesToUpdate = await _seriesService.GetSeries(resource.SeriesIds);
            var seriesToMove = new List<BulkMoveSeries>();

            foreach (var series in seriesToUpdate)
            {
                if (resource.Monitored.HasValue)
                {
                    series.Monitored = resource.Monitored.Value;
                }

                if (resource.MonitorNewItems.HasValue)
                {
                    series.MonitorNewItems = resource.MonitorNewItems.Value;
                }

                if (resource.QualityProfileId.HasValue)
                {
                    series.QualityProfileId = resource.QualityProfileId.Value;
                }

                if (resource.SeriesType.HasValue)
                {
                    series.SeriesType = resource.SeriesType.Value;
                }

                if (resource.SeasonFolder.HasValue)
                {
                    series.SeasonFolder = resource.SeasonFolder.Value;
                }

                if (resource.RootFolderPath.IsNotNullOrWhiteSpace())
                {
                    series.RootFolderPath = resource.RootFolderPath;
                    seriesToMove.Add(new BulkMoveSeries
                    {
                        SeriesId = series.Id,
                        SourcePath = series.Path
                    });
                }

                if (resource.Tags != null)
                {
                    var newTags = resource.Tags;
                    var applyTags = resource.ApplyTags;

                    switch (applyTags)
                    {
                        case ApplyTags.Add:
                            newTags.ForEach(t => series.Tags.Add(t));
                            break;
                        case ApplyTags.Remove:
                            newTags.ForEach(t => series.Tags.Remove(t));
                            break;
                        case ApplyTags.Replace:
                            series.Tags = new HashSet<int>(newTags);
                            break;
                    }
                }

                var validationResult = await _seriesEditorValidator.ValidateAsync(series);

                if (!validationResult.IsValid)
                {
                    throw new ValidationException(validationResult.Errors);
                }
            }

            if (resource.MoveFiles && seriesToMove.Any())
            {
                await _commandQueueManager.Push(new BulkMoveSeriesCommand
                {
                    DestinationRootFolder = resource.RootFolderPath,
                    Series = seriesToMove
                });
            }

            return Accepted((await _seriesService.UpdateSeries(seriesToUpdate, !resource.MoveFiles)).ToResource());
        }

        [HttpDelete]
        public async Task<object> DeleteSeries([FromBody] SeriesEditorResource resource)
        {
            await _seriesService.DeleteSeries(resource.SeriesIds, resource.DeleteFiles, resource.AddImportListExclusion);

            return new { };
        }
    }
}
