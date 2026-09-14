using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Tags;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Profiles.Release;

[V5ApiController]
public class ReleaseProfileController : RestController<ReleaseProfileResource>
{
    private readonly IReleaseProfileService _releaseProfileService;

    public ReleaseProfileController(IReleaseProfileService releaseProfileService, IIndexerFactory indexerFactory, ITagService tagService)
    {
        _releaseProfileService = releaseProfileService;

        SharedValidator.RuleFor(d => d).Custom((restriction, context) =>
        {
            if (restriction.Required.Empty() && restriction.Ignored.Empty() && !restriction.AirDateRestriction && !restriction.AllowSeasonPackWithoutAllEpisodesAired)
            {
                context.AddFailure(nameof(ReleaseProfileResource.Required), "'Must contain' or 'Must not contain' is required");
            }

            if (restriction.Required.Any(t => t.IsNullOrWhiteSpace()))
            {
                context.AddFailure(nameof(ReleaseProfileResource.Required), "'Must contain' should not contain whitespaces or an empty string");
            }

            if (restriction.Ignored.Any(t => t.IsNullOrWhiteSpace()))
            {
                context.AddFailure(nameof(ReleaseProfileResource.Ignored), "'Must not contain' should not contain whitespaces or an empty string");
            }

            // NOTE: FluentValidation's synchronous `Custom()` callback can't await; the request
            // validation pipeline (RestController.ValidateResource) is itself synchronous
            // framework code out of scope for this pass, so we bridge here as the documented
            // boundary (see ProviderControllerBase.cs).
            if (restriction is { Enabled: true, IndexerIds.Count: > 0 })
            {
                foreach (var indexerId in restriction.IndexerIds.Where(indexerId => !indexerFactory.Exists(indexerId).GetAwaiter().GetResult()))
                {
                    context.AddFailure(nameof(ReleaseProfileResource.IndexerIds), $"Indexer does not exist: {indexerId}");
                }
            }
        });

        SharedValidator.RuleFor(d => d.Tags.Intersect(d.ExcludedTags))
            .Empty()
            .WithName("ExcludedTags")
            .WithMessage(d => $"'{string.Join(", ", tagService.GetTags(d.Tags.Intersect(d.ExcludedTags)).GetAwaiter().GetResult().Select(t => t.Label))}' cannot be in both 'Tags' and 'Excluded Tags'");
    }

    [RestPostById]
    [Consumes("application/json")]
    public async Task<Results<Created<ReleaseProfileResource>, NotFound>> Create([FromBody] ReleaseProfileResource resource)
    {
        var model = resource.ToModel();
        model = await _releaseProfileService.Add(model);
        return TypedCreated(model.Id);
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteProfile(int id)
    {
        await _releaseProfileService.Delete(id);

        return TypedResults.NoContent();
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<ReleaseProfileResource>, NotFound>> Update([FromBody] ReleaseProfileResource resource)
    {
        var model = resource.ToModel();

        await _releaseProfileService.Update(model);

        return TypedAccepted(model.Id);
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override ReleaseProfileResource GetResourceById(int id)
    {
        return _releaseProfileService.Get(id).GetAwaiter().GetResult().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<ReleaseProfileResource>>> GetAll()
    {
        return TypedResults.Ok((await _releaseProfileService.All()).ToResource());
    }
}
