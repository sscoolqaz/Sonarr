using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Profiles.Qualities;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Profiles.Quality
{
    [V3ApiController]
    public class QualityProfileController : RestController<QualityProfileResource>
    {
        private readonly IQualityProfileService _profileService;

        public QualityProfileController(IQualityProfileService profileService, ICustomFormatService formatService)
        {
            _profileService = profileService;
            SharedValidator.RuleFor(c => c.Name).NotEmpty();

            SharedValidator.RuleFor(c => c.MinUpgradeFormatScore).GreaterThanOrEqualTo(1);
            SharedValidator.RuleFor(c => c.Cutoff).ValidCutoff();
            SharedValidator.RuleFor(c => c.Items).ValidItems();

            // NOTE: FluentValidation's synchronous `Must()` predicate can't await; the request
            // validation pipeline (RestController.ValidateResource) is itself synchronous
            // framework code out of scope for this pass, so we bridge here as the documented
            // boundary (see ProviderControllerBase.cs) rather than converting FluentValidation's
            // Must to MustAsync app-wide.
            SharedValidator.RuleFor(c => c.FormatItems).Must(items =>
            {
                var all = formatService.All().GetAwaiter().GetResult().Select(f => f.Id).ToList();
                var ids = items.Select(i => i.Format);

                return all.Except(ids).Empty();
            }).WithMessage("All Custom Formats and no extra ones need to be present inside your Profile! Try refreshing your browser.");

            SharedValidator.RuleFor(c => c).Custom((profile, context) =>
            {
                if (profile.FormatItems.Where(x => x.Score > 0).Sum(x => x.Score) < profile.MinFormatScore &&
                    profile.FormatItems.Max(x => x.Score) < profile.MinFormatScore)
                {
                    context.AddFailure("Minimum Custom Format Score can never be satisfied");
                }
            });

            SharedValidator.RuleFor(c => c)
                .SetValidator(new QualityProfileResourceValidator());
        }

        [RestPostById]
        [Consumes("application/json")]
        public async Task<ActionResult<QualityProfileResource>> Create([FromBody] QualityProfileResource resource)
        {
            var model = resource.ToModel();
            model = await _profileService.Add(model);
            return Created(model.Id);
        }

        [RestDeleteById]
        public async Task DeleteProfile(int id)
        {
            await _profileService.Delete(id);
        }

        [RestPutById]
        [Consumes("application/json")]
        public async Task<ActionResult<QualityProfileResource>> Update([FromBody] QualityProfileResource resource)
        {
            var model = resource.ToModel();

            await _profileService.Update(model);

            return Accepted(model.Id);
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // TagController/RootFolderController).
        protected override QualityProfileResource GetResourceById(int id)
        {
            return _profileService.Get(id).GetAwaiter().GetResult().ToResource();
        }

        [HttpGet]
        [Produces("application/json")]
        public async Task<List<QualityProfileResource>> GetAll()
        {
            return (await _profileService.All()).ToResource();
        }
    }
}
