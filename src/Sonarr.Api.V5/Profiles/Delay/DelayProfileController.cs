using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Profiles.Delay;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;
using Sonarr.Http.Validation;

namespace Sonarr.Api.V5.Profiles.Delay;

[V5ApiController]
public class DelayProfileController : RestController<DelayProfileResource>
{
    private readonly IDelayProfileService _delayProfileService;

    public DelayProfileController(IDelayProfileService delayProfileService, DelayProfileTagInUseValidator tagInUseValidator)
    {
        _delayProfileService = delayProfileService;

        SharedValidator.RuleFor(d => d.Tags).NotEmpty().When(d => d.Id != 1);
        SharedValidator.RuleFor(d => d.Tags).EmptyCollection<DelayProfileResource, int>().When(d => d.Id == 1);
        SharedValidator.RuleFor(d => d.Tags).SetValidator(tagInUseValidator);
        SharedValidator.RuleFor(d => d.UsenetDelay).GreaterThanOrEqualTo(0);
        SharedValidator.RuleFor(d => d.TorrentDelay).GreaterThanOrEqualTo(0);

        SharedValidator.RuleFor(d => d).Custom((delayProfile, context) =>
        {
            if (!delayProfile.EnableUsenet && !delayProfile.EnableTorrent)
            {
                context.AddFailure("Either Usenet or Torrent should be enabled");
            }
        });
    }

    [RestPostById]
    [Consumes("application/json")]
    public async Task<Results<Created<DelayProfileResource>, NotFound>> Create([FromBody] DelayProfileResource resource)
    {
        var model = resource.ToModel();
        model = await _delayProfileService.Add(model);

        return TypedCreated(model.Id);
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteProfile(int id)
    {
        if (id == 1)
        {
            throw new MethodNotAllowedException("Cannot delete global delay profile");
        }

        await _delayProfileService.Delete(id);

        return TypedResults.NoContent();
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<DelayProfileResource>, NotFound>> Update([FromBody] DelayProfileResource resource)
    {
        var model = resource.ToModel();
        await _delayProfileService.Update(model);

        return TypedAccepted(model.Id);
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override DelayProfileResource GetResourceById(int id)
    {
        return _delayProfileService.Get(id).GetAwaiter().GetResult().ToResource()!;
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<DelayProfileResource>>> GetAll()
    {
        return TypedResults.Ok((await _delayProfileService.All()).ToResource());
    }

    [HttpPut("reorder/{id}")]
    [Produces("application/json")]
    public async Task<Ok<List<DelayProfileResource>>> Reorder([FromRoute] int id, [FromQuery] int? after)
    {
        ValidateId(id);

        return TypedResults.Ok((await _delayProfileService.Reorder(id, after)).ToResource());
    }
}
