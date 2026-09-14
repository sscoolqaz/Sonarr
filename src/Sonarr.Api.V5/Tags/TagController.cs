using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Tags;

[V5ApiController]
public class TagController : RestControllerWithSignalR<TagResource, Tag>,
                             IHandle<TagsUpdatedEvent>,
                             IHandle<AutoTagsUpdatedEvent>
{
    private readonly ITagService _tagService;

    public TagController(IBroadcastSignalRMessage signalRBroadcaster,
        ITagService tagService)
        : base(signalRBroadcaster)
    {
        _tagService = tagService;

        SharedValidator.RuleFor(c => c.Label).Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Matches("^[a-z0-9-]+$", RegexOptions.IgnoreCase)
            .WithMessage("Allowed characters a-z, 0-9 and -");
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override TagResource GetResourceById(int id)
    {
        return _tagService.GetTag(id).GetAwaiter().GetResult().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<TagResource>>> GetAll()
    {
        return TypedResults.Ok((await _tagService.All()).ToResource());
    }

    [RestPostById]
    [Consumes("application/json")]
    public async Task<Results<Created<TagResource>, NotFound>> Create([FromBody] TagResource resource)
    {
        return TypedCreated((await _tagService.Add(resource.ToModel())).Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<TagResource>, NotFound>> Update([FromBody] TagResource resource)
    {
        await _tagService.Update(resource.ToModel());
        return TypedAccepted(resource.Id);
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteTag(int id)
    {
        await _tagService.Delete(id);

        return TypedResults.NoContent();
    }

    [NonAction]
    public void Handle(TagsUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }

    [NonAction]
    public void Handle(AutoTagsUpdatedEvent message)
    {
        BroadcastResourceChange(ModelAction.Sync);
    }
}
