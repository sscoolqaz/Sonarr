using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tags;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V3.Tags
{
    [V3ApiController("tag/detail")]
    public class TagDetailsController : RestController<TagDetailsResource>
    {
        private readonly ITagService _tagService;

        public TagDetailsController(ITagService tagService)
        {
            _tagService = tagService;
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override TagDetailsResource GetResourceById(int id)
        {
            return _tagService.Details(id).GetAwaiter().GetResult().ToResource();
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<TagDetailsResource>> GetAll()
        {
            return (await _tagService.Details()).ToResource();
        }
    }
}
