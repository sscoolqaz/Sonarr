using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.CustomFilters;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.CustomFilters
{
    [V3ApiController]
    public class CustomFilterController : RestController<CustomFilterResource>
    {
        private readonly ICustomFilterService _customFilterService;

        public CustomFilterController(ICustomFilterService customFilterService)
        {
            _customFilterService = customFilterService;
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override CustomFilterResource GetResourceById(int id)
        {
            return _customFilterService.Get(id).GetAwaiter().GetResult().ToResource();
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<CustomFilterResource>> GetCustomFilters()
        {
            return (await _customFilterService.All()).ToResource();
        }

        [RestPostById]
        [Consumes("application/json")]
        public async Task<ActionResult<CustomFilterResource>> AddCustomFilter([FromBody] CustomFilterResource resource)
        {
            var customFilter = await _customFilterService.Add(resource.ToModel());

            return Created(customFilter.Id);
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<CustomFilterResource>> UpdateCustomFilter([FromBody] CustomFilterResource resource)
        {
            await _customFilterService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public async Task DeleteCustomResource(int id)
        {
            await _customFilterService.Delete(id);
        }
    }
}
