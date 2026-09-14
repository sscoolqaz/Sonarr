using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Validation.Paths
{
    public class RootFolderExistsValidator : PropertyValidator
    {
        private readonly IRootFolderService _rootFolderService;

        public RootFolderExistsValidator(IRootFolderService rootFolderService)
        {
            _rootFolderService = rootFolderService;
        }

        protected override string GetDefaultMessageTemplate() => "Root folder '{path}' does not exist";

        public override bool ShouldValidateAsynchronously(IValidationContext context) => true;

        protected override bool IsValid(PropertyValidatorContext context)
        {
            // Bridge for callers still on the synchronous FluentValidation path (see SeriesTitleSlugValidator).
            return IsValidAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        protected override async Task<bool> IsValidAsync(PropertyValidatorContext context, CancellationToken cancellation)
        {
            context.MessageFormatter.AppendArgument("path", context.PropertyValue?.ToString());

            if (context.PropertyValue == null)
            {
                return true;
            }

            var rootFolders = await _rootFolderService.All();

            return rootFolders.Exists(r => r.Path.IsPathValid(PathValidationType.CurrentOs) && r.Path.PathEquals(context.PropertyValue.ToString()));
        }
    }
}
