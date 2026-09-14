using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.MediaFiles;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Episodes
{
    [V3ApiController("rename")]
    public class RenameEpisodeController : Controller
    {
        private readonly IRenameEpisodeFileService _renameEpisodeFileService;

        public RenameEpisodeController(IRenameEpisodeFileService renameEpisodeFileService)
        {
            _renameEpisodeFileService = renameEpisodeFileService;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<RenameEpisodeResource>> GetEpisodes(int seriesId, int? seasonNumber)
        {
            if (seasonNumber.HasValue)
            {
                return (await _renameEpisodeFileService.GetRenamePreviews(seriesId, seasonNumber.Value)).ToResource();
            }

            return (await _renameEpisodeFileService.GetRenamePreviews(seriesId)).ToResource();
        }

        [HttpGet("bulk")]
        [Produces("application/json")]
        public async Task<List<RenameEpisodeResource>> GetEpisodes([FromQuery] List<int> seriesIds)
        {
            if (seriesIds is { Count: 0 })
            {
                throw new BadRequestException("seriesIds must be provided");
            }

            if (seriesIds.Any(seriesId => seriesId <= 0))
            {
                throw new BadRequestException("seriesIds must be positive integers");
            }

            return (await _renameEpisodeFileService.GetRenamePreviews(seriesIds)).ToResource();
        }
    }
}
