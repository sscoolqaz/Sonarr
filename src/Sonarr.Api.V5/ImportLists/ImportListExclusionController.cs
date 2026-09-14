using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.ImportLists.Exclusions;
using Sonarr.Http;
using Sonarr.Http.Extensions;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.ImportLists;

[V5ApiController]
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
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override ImportListExclusionResource GetResourceById(int id)
    {
        return _importListExclusionService.Get(id).GetAwaiter().GetResult().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<PagingResource<ImportListExclusionResource>>> GetImportListExclusions([FromQuery] PagingRequestResource paging)
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

        return TypedResults.Ok(pageSpec.ApplyToPage(s => pagedResult, ImportListExclusionResourceMapper.ToResource));
    }

    [RestPostById]
    [Consumes("application/json")]
    public async Task<Results<Created<ImportListExclusionResource>, NotFound>> AddImportListExclusion([FromBody] ImportListExclusionResource resource)
    {
        var importListExclusion = await _importListExclusionService.Add(resource.ToModel());

        return TypedCreated(importListExclusion.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<ImportListExclusionResource>, NotFound>> UpdateImportListExclusion([FromBody] ImportListExclusionResource resource)
    {
        await _importListExclusionService.Update(resource.ToModel());

        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteImportListExclusion(int id)
    {
        await _importListExclusionService.Delete(id);

        return TypedResults.NoContent();
    }

    [HttpDelete("bulk")]
    [Consumes("application/json")]
    public async Task<NoContent> DeleteImportListExclusions([FromBody] ImportListExclusionBulkResource resource)
    {
        await _importListExclusionService.Delete(resource.Ids.ToList());

        return TypedResults.NoContent();
    }
}
