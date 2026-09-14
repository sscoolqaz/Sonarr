using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.SignalR;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Tags
{
    [V3ApiController]
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
        public async Task<List<TagResource>> GetAll()
        {
            return (await _tagService.All()).ToResource();
        }

        [RestPostById]
        [Consumes("application/json")]
        public async Task<ActionResult<TagResource>> Create([FromBody] TagResource resource)
        {
            return Created((await _tagService.Add(resource.ToModel())).Id);
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<TagResource>> Update([FromBody] TagResource resource)
        {
            await _tagService.Update(resource.ToModel());
            return Accepted(resource.Id);
        }

        [RestDeleteById]
        public async Task DeleteTag(int id)
        {
            await _tagService.Delete(id);
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
}
