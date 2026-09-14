using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Core.ImportLists.Exclusions;

namespace Sonarr.Api.V3.ImportLists
{
    public class ImportListExclusionExistsValidator : PropertyValidator
    {
        private readonly IImportListExclusionService _importListExclusionService;

        public ImportListExclusionExistsValidator(IImportListExclusionService importListExclusionService)
        {
            _importListExclusionService = importListExclusionService;
        }

        protected override string GetDefaultMessageTemplate() => "This exclusion has already been added.";

        public override bool ShouldValidateAsynchronously(IValidationContext context) => true;

        protected override bool IsValid(PropertyValidatorContext context)
        {
            // Bridge for callers still on the synchronous FluentValidation path (see RootFolderValidator).
            return IsValidAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        protected override async Task<bool> IsValidAsync(PropertyValidatorContext context, CancellationToken cancellation)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            if (context.InstanceToValidate is not ImportListExclusionResource listExclusionResource)
            {
                return true;
            }

            var all = await _importListExclusionService.All();

            return !all.Exists(v => v.TvdbId == (int)context.PropertyValue && v.Id != listExclusionResource.Id);
        }
    }
}
