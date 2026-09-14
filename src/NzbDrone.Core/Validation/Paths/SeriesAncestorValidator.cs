using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Validation.Paths
{
    public class SeriesAncestorValidator : PropertyValidator
    {
        private readonly ISeriesService _seriesService;

        public SeriesAncestorValidator(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is an ancestor of an existing series";

        public override bool ShouldValidateAsynchronously(IValidationContext context) => true;

        protected override bool IsValid(PropertyValidatorContext context)
        {
            // Bridge for callers still on the synchronous FluentValidation path (see SeriesTitleSlugValidator).
            return IsValidAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        protected override async Task<bool> IsValidAsync(PropertyValidatorContext context, CancellationToken cancellation)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("path", context.PropertyValue.ToString());

            var allSeriesPaths = await _seriesService.GetAllSeriesPaths();

            return !allSeriesPaths.Any(s => context.PropertyValue.ToString().IsParentPath(s.Value));
        }
    }
}
