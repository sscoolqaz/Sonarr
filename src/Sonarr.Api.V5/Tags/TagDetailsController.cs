using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Tags;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Tags;

[V5ApiController("tag/detail")]
public class TagDetailsController : RestController<TagDetailsResource>
{
    private readonly ITagService _tagService;

    public TagDetailsController(ITagService tagService)
    {
        _tagService = tagService;
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override TagDetailsResource GetResourceById(int id)
    {
        return _tagService.Details(id).GetAwaiter().GetResult().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<TagDetailsResource>>> GetAll()
    {
        return TypedResults.Ok((await _tagService.Details()).ToResource());
    }
}
