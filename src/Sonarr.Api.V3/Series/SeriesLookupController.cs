using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.SeriesStats;
using Sonarr.Http;

namespace Sonarr.Api.V3.Series
{
    [V3ApiController("series/lookup")]
    public class SeriesLookupController : Controller
    {
        private readonly ISearchForNewSeries _searchProxy;
        private readonly IBuildFileNames _fileNameBuilder;
        private readonly IMapCoversToLocal _coverMapper;

        public SeriesLookupController(ISearchForNewSeries searchProxy, IBuildFileNames fileNameBuilder, IMapCoversToLocal coverMapper)
        {
            _searchProxy = searchProxy;
            _fileNameBuilder = fileNameBuilder;
            _coverMapper = coverMapper;
        }

        [HttpGet]
        public async Task<IEnumerable<SeriesResource>> Search([FromQuery] string term)
        {
            var tvDbResults = _searchProxy.SearchForNewSeries(term);
            return await MapToResource(tvDbResults);
        }

        private async Task<List<SeriesResource>> MapToResource(IEnumerable<NzbDrone.Core.Tv.Series> series)
        {
            var result = new List<SeriesResource>();

            foreach (var currentSeries in series)
            {
                var resource = currentSeries.ToResource();

                _coverMapper.ConvertToLocalUrls(resource.Id, resource.Images);

                var poster = currentSeries.Images.FirstOrDefault(c => c.CoverType == MediaCoverTypes.Poster);

                if (poster != null)
                {
                    resource.RemotePoster = poster.RemoteUrl;
                }

                resource.Folder = await _fileNameBuilder.GetSeriesFolder(currentSeries);
                resource.Statistics = new SeriesStatistics().ToResource(resource.Seasons);

                result.Add(resource);
            }

            return result;
        }
    }
}
