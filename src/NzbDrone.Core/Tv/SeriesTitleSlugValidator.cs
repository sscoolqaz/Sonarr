using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Tv
{
    public class SeriesTitleSlugValidator : PropertyValidator
    {
        private readonly ISeriesService _seriesService;

        public SeriesTitleSlugValidator(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        protected override string GetDefaultMessageTemplate() =>
            "Title slug '{slug}' is in use by series '{seriesTitle}'. Check the FAQ for more information";

        public override bool ShouldValidateAsynchronously(IValidationContext context) => true;

        protected override bool IsValid(PropertyValidatorContext context)
        {
            // Bridge for the small number of callers still on the synchronous FluentValidation path
            // (e.g. AddSeriesValidator/AddSeriesService, not converted this phase). Safe: FluentValidation
            // invokes this either from an ASP.NET Core request thread (no SynchronizationContext) or a
            // background command-processor thread, so this can't deadlock - same reasoning already applied
            // to IHandle bridging this session.
            return IsValidAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        protected override async Task<bool> IsValidAsync(PropertyValidatorContext context, CancellationToken cancellation)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            dynamic instance = context.ParentContext.InstanceToValidate;
            var instanceId = (int)instance.Id;
            var slug = context.PropertyValue.ToString();

            var allSeries = await _seriesService.GetAllSeries();

            var conflictingSeries = allSeries.FirstOrDefault(s => s.TitleSlug.IsNotNullOrWhiteSpace() &&
                                                        s.TitleSlug.Equals(context.PropertyValue.ToString()) &&
                                                        s.Id != instanceId);

            if (conflictingSeries == null)
            {
                return true;
            }

            context.MessageFormatter.AppendArgument("slug", slug);
            context.MessageFormatter.AppendArgument("seriesTitle", conflictingSeries.Title);

            return false;
        }
    }
}
