using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv;
using Sonarr.Http;

namespace Sonarr.Api.V3.Series;

[V3ApiController("series")]
public class SeriesFolderController : Controller
{
    private readonly ISeriesService _seriesService;
    private readonly IBuildFileNames _fileNameBuilder;

    public SeriesFolderController(ISeriesService seriesService, IBuildFileNames fileNameBuilder)
    {
        _seriesService = seriesService;
        _fileNameBuilder = fileNameBuilder;
    }

    [HttpGet("{id}/folder")]
    public async Task<object> GetFolder([FromRoute] int id)
    {
        var series = await _seriesService.GetSeries(id);
        var folder = await _fileNameBuilder.GetSeriesFolder(series);

        return new
        {
            folder
        };
    }
}
