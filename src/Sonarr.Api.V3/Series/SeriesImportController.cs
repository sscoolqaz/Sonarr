using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V3.Series
{
    [V3ApiController("series/import")]
    public class SeriesImportController : Controller
    {
        private readonly IAddSeriesService _addSeriesService;

        public SeriesImportController(IAddSeriesService addSeriesService)
        {
            _addSeriesService = addSeriesService;
        }

        [HttpPost]
        public async Task<object> Import([FromBody] List<SeriesResource> resource)
        {
            var newSeries = resource.ToModel();

            return (await _addSeriesService.AddSeries(newSeries)).ToResource();
        }
    }
}
