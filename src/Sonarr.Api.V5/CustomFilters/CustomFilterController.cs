using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.CustomFilters;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.CustomFilters;

[V5ApiController]
public class CustomFilterController : RestController<CustomFilterResource>
{
    private readonly ICustomFilterService _customFilterService;

    public CustomFilterController(ICustomFilterService customFilterService)
    {
        _customFilterService = customFilterService;
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override CustomFilterResource GetResourceById(int id)
    {
        return _customFilterService.Get(id).GetAwaiter().GetResult().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<CustomFilterResource>>> GetCustomFilters()
    {
        return TypedResults.Ok((await _customFilterService.All()).ToResource());
    }

    [RestPostById]
    [Consumes("application/json")]
    public async Task<Results<Created<CustomFilterResource>, NotFound>> AddCustomFilter([FromBody] CustomFilterResource resource)
    {
        var customFilter = await _customFilterService.Add(resource.ToModel());

        return TypedCreated(customFilter.Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<CustomFilterResource>, NotFound>> UpdateCustomFilter([FromBody] CustomFilterResource resource)
    {
        await _customFilterService.Update(resource.ToModel());
        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteCustomResource(int id)
    {
        await _customFilterService.Delete(id);

        return TypedResults.NoContent();
    }
}
