using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;

namespace Sonarr.Api.V3.Series
{
    public class SeriesFolderAsRootFolderValidator : PropertyValidator
    {
        private readonly IBuildFileNames _fileNameBuilder;

        public SeriesFolderAsRootFolderValidator(IBuildFileNames fileNameBuilder)
        {
            _fileNameBuilder = fileNameBuilder;
        }

        protected override string GetDefaultMessageTemplate() => "Root folder path '{rootFolderPath}' contains series folder '{seriesFolder}'";

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

            if (context.InstanceToValidate is not SeriesResource seriesResource)
            {
                return true;
            }

            var rootFolderPath = context.PropertyValue.ToString();

            if (rootFolderPath.IsNullOrWhiteSpace())
            {
                return true;
            }

            var rootFolder = new DirectoryInfo(rootFolderPath!).Name;
            var series = seriesResource.ToModel();
            var seriesFolder = await _fileNameBuilder.GetSeriesFolder(series);

            context.MessageFormatter.AppendArgument("rootFolderPath", rootFolderPath);
            context.MessageFormatter.AppendArgument("seriesFolder", seriesFolder);

            if (seriesFolder == rootFolder)
            {
                return false;
            }

            var distance = seriesFolder.LevenshteinDistance(rootFolder);

            return distance >= Math.Max(1, seriesFolder.Length * 0.2);
        }
    }
}
