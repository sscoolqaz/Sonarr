using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Validation;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.CustomFormats
{
    [V3ApiController]
    public class CustomFormatController : RestController<CustomFormatResource>
    {
        private readonly ICustomFormatService _formatService;
        private readonly List<ICustomFormatSpecification> _specifications;

        public CustomFormatController(ICustomFormatService formatService,
                                  List<ICustomFormatSpecification> specifications)
        {
            _formatService = formatService;
            _specifications = specifications;

            SharedValidator.RuleFor(c => c.Name).NotEmpty();

            // NOTE: FluentValidation's synchronous `Must()` predicate can't await; the request
            // validation pipeline (RestController.ValidateResource) is itself synchronous
            // framework code out of scope for this pass, so we bridge here as the documented
            // boundary (see ProviderControllerBase.cs) rather than converting FluentValidation's
            // Must to MustAsync app-wide.
            SharedValidator.RuleFor(c => c.Name)
                .Must((v, c) => !_formatService.All().GetAwaiter().GetResult().Any(f => f.Name == c && f.Id != v.Id)).WithMessage("Must be unique.");
            SharedValidator.RuleFor(c => c.Specifications).NotEmpty();
            SharedValidator.RuleFor(c => c).Custom((customFormat, context) =>
            {
                if (!customFormat.Specifications.Any())
                {
                    context.AddFailure("Must contain at least one Condition");
                }

                if (customFormat.Specifications.Any(s => s.Name.IsNullOrWhiteSpace()))
                {
                    context.AddFailure("Condition name(s) cannot be empty or consist of only spaces");
                }
            });
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override CustomFormatResource GetResourceById(int id)
        {
            return _formatService.GetById(id).GetAwaiter().GetResult().ToResource(true);
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<CustomFormatResource>> GetAll()
        {
            return (await _formatService.All()).ToResource(true);
        }

        [RestPostById]
        [Consumes("application/json")]
        public async Task<ActionResult<CustomFormatResource>> Create([FromBody] CustomFormatResource customFormatResource)
        {
            var model = customFormatResource.ToModel(_specifications);

            Validate(model);

            return Created((await _formatService.Insert(model)).Id);
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<CustomFormatResource>> Update([FromBody] CustomFormatResource resource)
        {
            var model = resource.ToModel(_specifications);

            Validate(model);

            await _formatService.Update(model);

            return Accepted(model.Id);
        }

        [HttpPut("bulk")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public virtual async Task<ActionResult<CustomFormatResource>> Update([FromBody] CustomFormatBulkResource resource)
        {
            if (!resource.Ids.Any())
            {
                throw new BadRequestException("ids must be provided");
            }

            var customFormats = new List<CustomFormat>();

            foreach (var id in resource.Ids)
            {
                customFormats.Add(await _formatService.GetById(id));
            }

            customFormats.ForEach(existing =>
            {
                existing.IncludeCustomFormatWhenRenaming = resource.IncludeCustomFormatWhenRenaming ?? existing.IncludeCustomFormatWhenRenaming;
            });

            await _formatService.Update(customFormats);

            return Accepted(customFormats.ConvertAll(cf => cf.ToResource(true)));
        }

        [RestDeleteById]
        public async Task DeleteFormat(int id)
        {
            await _formatService.Delete(id);
        }

        [HttpDelete("bulk")]
        [Consumes("application/json")]
        public virtual async Task<object> DeleteFormats([FromBody] CustomFormatBulkResource resource)
        {
            await _formatService.Delete(resource.Ids.ToList());

            return new { };
        }

        [HttpGet("schema")]
        public async Task<object> GetTemplates()
        {
            var schema = _specifications.OrderBy(x => x.Order).Select(x => x.ToSchema()).ToList();

            var presets = (await GetPresets()).ToList();

            foreach (var item in schema)
            {
                item.Presets = presets.Where(x => x.GetType().Name == item.Implementation).Select(x => x.ToSchema()).ToList();
            }

            return schema;
        }

        private void Validate(CustomFormat definition)
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

        private async Task<List<ICustomFormatSpecification>> GetPresets()
        {
            var presets = new List<ICustomFormatSpecification>
            {
                new ReleaseTitleSpecification
                {
                    Name = "x264",
                    Value = @"(x|h)\.?264"
                },
                new ReleaseTitleSpecification
                {
                    Name = "x265",
                    Value = @"(((x|h)\.?265)|(HEVC))"
                },
                new ReleaseTitleSpecification
                {
                    Name = "Simple Hardcoded Subs",
                    Value = @"subs?"
                },
                new ReleaseTitleSpecification
                {
                    Name = "Hardcoded Subs",
                    Value = @"\b(?<hcsub>(\w+SUBS?)\b)|(?<hc>(HC|SUBBED))\b"
                },
                new ReleaseTitleSpecification
                {
                    Name = "Surround Sound",
                    Value = @"DTS.?(HD|ES|X(?!\D))|TRUEHD|ATMOS|DD(\+|P).?([5-9])|EAC3.?([5-9])"
                },
                new ReleaseTitleSpecification
                {
                    Name = "Preferred Words",
                    Value = @"\b(SPARKS|Framestor)\b"
                }
            };

            var formats = await _formatService.All();
            foreach (var format in formats)
            {
                foreach (var condition in format.Specifications)
                {
                    var preset = condition.Clone();
                    preset.Name = $"{format.Name}: {preset.Name}";
                    presets.Add(preset);
                }
            }

            return presets;
        }
    }
}
