using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Validation;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.AutoTagging;

[V5ApiController]
public class AutoTaggingController : RestController<AutoTaggingResource>
{
    private readonly IAutoTaggingService _autoTaggingService;
    private readonly List<IAutoTaggingSpecification> _specifications;

    public AutoTaggingController(IAutoTaggingService autoTaggingService,
                                 List<IAutoTaggingSpecification> specifications)
    {
        _autoTaggingService = autoTaggingService;
        _specifications = specifications;

        SharedValidator.RuleFor(c => c.Name).NotEmpty();

        // NOTE: FluentValidation's synchronous `Must()` predicate can't await; the request
        // validation pipeline (RestController.ValidateResource) is itself synchronous framework
        // code out of scope for this pass, so we bridge here as the documented boundary (see
        // ProviderControllerBase.cs).
        SharedValidator.RuleFor(c => c.Name)
            .Must((v, c) => !_autoTaggingService.All().GetAwaiter().GetResult().Any(f => f.Name == c && f.Id != v.Id)).WithMessage("Must be unique.");
        SharedValidator.RuleFor(c => c.Tags).NotEmpty();
        SharedValidator.RuleFor(c => c).Custom((autoTag, context) =>
        {
            if (!autoTag.Specifications.Any())
            {
                context.AddFailure("Must contain at least one Condition");
            }

            if (autoTag.Specifications.Any(s => s.Name.IsNullOrWhiteSpace()))
            {
                context.AddFailure("Condition name(s) cannot be empty or consist of only spaces");
            }
        });
    }

    // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override AutoTaggingResource GetResourceById(int id)
    {
        return _autoTaggingService.GetById(id).GetAwaiter().GetResult().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<List<AutoTaggingResource>>> GetAll()
    {
        return TypedResults.Ok((await _autoTaggingService.All()).ToResource());
    }

    [RestPostById]
    [Consumes("application/json")]
    public async Task<Results<Created<AutoTaggingResource>, NotFound>> Create([FromBody] AutoTaggingResource autoTagResource)
    {
        var model = autoTagResource.ToModel(_specifications);

        Validate(model);

        return TypedCreated((await _autoTaggingService.Insert(model)).Id);
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<AutoTaggingResource>, NotFound>> Update([FromBody] AutoTaggingResource resource)
    {
        var model = resource.ToModel(_specifications);

        Validate(model);

        await _autoTaggingService.Update(model);

        return TypedAccepted(model.Id);
    }

    [RestDeleteById]
    public async Task<NoContent> DeleteAutoTagging(int id)
    {
        await _autoTaggingService.Delete(id);

        return TypedResults.NoContent();
    }

    [HttpGet("schema")]
    [Produces("application/json")]
    public Ok<List<AutoTaggingSpecificationSchema>> GetTemplates()
    {
        return TypedResults.Ok(_specifications.OrderBy(x => x.Order).Select(x => x.ToSchema()).ToList());
    }

    private void Validate(AutoTag definition)
    {
        foreach (var validationResult in definition.Specifications.Select(spec => spec.Validate()))
        {
            VerifyValidationResult(validationResult);
        }
    }

    private void VerifyValidationResult(ValidationResult validationResult)
    {
        var result = new NzbDroneValidationResult(validationResult.Errors);

        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}
