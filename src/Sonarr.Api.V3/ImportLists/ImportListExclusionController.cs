using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists.Exclusions;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.ImportLists
{
    [V3ApiController]
    public class ImportListExclusionController : RestController<ImportListExclusionResource>
    {
        private readonly IImportListExclusionService _importListExclusionService;

        public ImportListExclusionController(IImportListExclusionService importListExclusionService,
                                             ImportListExclusionExistsValidator importListExclusionExistsValidator)
        {
            _importListExclusionService = importListExclusionService;

            SharedValidator.RuleFor(c => c.TvdbId).Cascade(CascadeMode.Stop)
                .NotEmpty()
                .SetValidator(importListExclusionExistsValidator);

            SharedValidator.RuleFor(c => c.Title).NotEmpty();
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override ImportListExclusionResource GetResourceById(int id)
        {
            return _importListExclusionService.Get(id).GetAwaiter().GetResult().ToResource();
        }

        [HttpGet]
        [Produces("application/json")]
        [Obsolete("Deprecated")]
        public async Task<List<ImportListExclusionResource>> GetImportListExclusions()
        {
            return (await _importListExclusionService.All()).ToResource();
        }

        [HttpGet("paged")]
        [Produces("application/json")]
        public async Task<PagingResource<ImportListExclusionResource>> GetImportListExclusionsPaged([FromQuery] PagingRequestResource paging)
        {
            var pagingResource = new PagingResource<ImportListExclusionResource>(paging);
            var pageSpec = pagingResource.MapToPagingSpec<ImportListExclusionResource, ImportListExclusion>(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "id",
                    "title",
                    "tvdbId"
                },
                "id",
                SortDirection.Descending);

            var pagedResult = await _importListExclusionService.Paged(pageSpec);

            return pageSpec.ApplyToPage(p => pagedResult, ImportListExclusionResourceMapper.ToResource);
        }

        [RestPostById]
        [Consumes("application/json")]
        public async Task<ActionResult<ImportListExclusionResource>> AddImportListExclusion([FromBody] ImportListExclusionResource resource)
        {
            var importListExclusion = await _importListExclusionService.Add(resource.ToModel());

            return Created(importListExclusion.Id);
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<ImportListExclusionResource>> UpdateImportListExclusion([FromBody] ImportListExclusionResource resource)
        {
            await _importListExclusionService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public async Task DeleteImportListExclusion(int id)
        {
            await _importListExclusionService.Delete(id);
        }

        [HttpDelete("bulk")]
        [Produces("application/json")]
        public async Task<object> DeleteImportListExclusions([FromBody] ImportListExclusionBulkResource resource)
        {
            await _importListExclusionService.Delete(resource.Ids.ToList());

            return new { };
        }
    }
}
