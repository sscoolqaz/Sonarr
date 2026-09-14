using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.DiskSpace;
using Sonarr.Http;

namespace Sonarr.Api.V3.DiskSpace
{
    [V3ApiController("diskspace")]
    public class DiskSpaceController : Controller
    {
        private readonly IDiskSpaceService _diskSpaceService;

        public DiskSpaceController(IDiskSpaceService diskSpaceService)
        {
            _diskSpaceService = diskSpaceService;
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<DiskSpaceResource>> GetFreeSpace()
        {
            return (await _diskSpaceService.GetFreeSpace()).ConvertAll(DiskSpaceResourceMapper.MapToResource);
        }
    }
}
