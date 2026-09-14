using FluentValidation.Validators;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Validation
{
    public class QualityProfileExistsValidator : PropertyValidator
    {
        private readonly IQualityProfileService _qualityProfileService;

        public QualityProfileExistsValidator(IQualityProfileService qualityProfileService)
        {
            _qualityProfileService = qualityProfileService;
        }

        protected override string GetDefaultMessageTemplate() => "Quality Profile does not exist";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context?.PropertyValue == null || (int)context.PropertyValue == 0)
            {
                return true;
            }

            // FluentValidation's PropertyValidator.IsValid is a synchronous framework seam - bridging
            // is safe here: runs off the request thread, no SynchronizationContext to deadlock against.
            return _qualityProfileService.Exists((int)context.PropertyValue).GetAwaiter().GetResult();
        }
    }
}
