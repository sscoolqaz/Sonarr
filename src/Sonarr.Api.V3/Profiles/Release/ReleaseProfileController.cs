using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Tags;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Profiles.Release
{
    [V3ApiController]
    public class ReleaseProfileController : RestController<ReleaseProfileResource>
    {
        private readonly IReleaseProfileService _releaseProfileService;

        public ReleaseProfileController(IReleaseProfileService releaseProfileService, IIndexerFactory indexerFactory, ITagService tagService)
        {
            _releaseProfileService = releaseProfileService;

            // NOTE: FluentValidation's synchronous `Custom()`/`WithMessage()` callbacks can't
            // await; the request validation pipeline (RestController.ValidateResource) is itself
            // synchronous framework code out of scope for this pass, so we bridge here as the
            // documented boundary (see ProviderControllerBase.cs) rather than converting
            // FluentValidation's Custom to CustomAsync app-wide.
            SharedValidator.RuleFor(d => d).Custom((restriction, context) =>
            {
                if (restriction.MapRequired().Empty() && restriction.MapIgnored().Empty() && !restriction.AirDateRestriction && !restriction.AllowSeasonPackWithoutAllEpisodesAired)
                {
                    context.AddFailure(nameof(ReleaseProfileResource.Required), "'Must contain' or 'Must not contain' is required");
                }

                if (restriction.MapRequired().Any(t => t.IsNullOrWhiteSpace()))
                {
                    context.AddFailure(nameof(ReleaseProfileResource.Required), "'Must contain' should not contain whitespaces or an empty string");
                }

                if (restriction.MapIgnored().Any(t => t.IsNullOrWhiteSpace()))
                {
                    context.AddFailure(nameof(ReleaseProfileResource.Ignored), "'Must not contain' should not contain whitespaces or an empty string");
                }

                if (restriction.Enabled && restriction.IndexerId != 0 && !indexerFactory.Exists(restriction.IndexerId).GetAwaiter().GetResult())
                {
                    context.AddFailure(nameof(ReleaseProfileResource.IndexerId), "Indexer does not exist");
                }
            });

            SharedValidator.RuleFor(d => d.Tags.Intersect(d.ExcludedTags))
                .Empty()
                .WithName("ExcludedTags")
                .WithMessage(d => $"'{string.Join(", ", tagService.GetTags(d.Tags.Intersect(d.ExcludedTags)).GetAwaiter().GetResult().Select(t => t.Label))}' cannot be in both 'Tags' and 'Excluded Tags'");
        }

        [RestPostById]
        public async Task<ActionResult<ReleaseProfileResource>> Create([FromBody] ReleaseProfileResource resource)
        {
            var model = resource.ToModel();
            model = await _releaseProfileService.Add(model);
            return Created(model.Id);
        }

        [RestDeleteById]
        public async Task DeleteProfile(int id)
        {
            await _releaseProfileService.Delete(id);
        }

        [RestPutById]
        public async Task<ActionResult<ReleaseProfileResource>> Update([FromBody] ReleaseProfileResource resource)
        {
            var model = resource.ToModel();

            await _releaseProfileService.Update(model);

            return Accepted(model.Id);
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override ReleaseProfileResource GetResourceById(int id)
        {
            return _releaseProfileService.Get(id).GetAwaiter().GetResult().ToResource();
        }

        [HttpGet]
        public async Task<List<ReleaseProfileResource>> GetAll()
        {
            return (await _releaseProfileService.All()).ToResource();
        }
    }
}
