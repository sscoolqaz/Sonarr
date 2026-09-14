using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V3.Config
{
    [V3ApiController("config/naming")]
    public class NamingConfigController : RestController<NamingConfigResource>
    {
        private readonly INamingConfigService _namingConfigService;
        private readonly IFilenameSampleService _filenameSampleService;
        private readonly IFilenameValidationService _filenameValidationService;
        private readonly IBuildFileNames _filenameBuilder;

        public NamingConfigController(INamingConfigService namingConfigService,
                                  IFilenameSampleService filenameSampleService,
                                  IFilenameValidationService filenameValidationService,
                                  IBuildFileNames filenameBuilder)
        {
            _namingConfigService = namingConfigService;
            _filenameSampleService = filenameSampleService;
            _filenameValidationService = filenameValidationService;
            _filenameBuilder = filenameBuilder;

            SharedValidator.RuleFor(c => c.MultiEpisodeStyle).InclusiveBetween(0, 5);
            SharedValidator.RuleFor(c => c.StandardEpisodeFormat).ValidEpisodeFormat();
            SharedValidator.RuleFor(c => c.DailyEpisodeFormat).ValidDailyEpisodeFormat();
            SharedValidator.RuleFor(c => c.AnimeEpisodeFormat).ValidAnimeEpisodeFormat();
            SharedValidator.RuleFor(c => c.SeriesFolderFormat).ValidSeriesFolderFormat();
            SharedValidator.RuleFor(c => c.SeasonFolderFormat).ValidSeasonFolderFormat();
            SharedValidator.RuleFor(c => c.SpecialsFolderFormat).ValidSpecialsFolderFormat();
            SharedValidator.RuleFor(c => c.CustomColonReplacementFormat).ValidCustomColonReplacement().When(c => c.ColonReplacementFormat == (int)ColonReplacementFormat.Custom);
        }

        // NOTE: RestController<TResource>.GetResourceById is a synchronous framework hook used
        // app-wide; blocking here via GetAwaiter().GetResult() is the documented boundary (see
        // ProviderControllerBase.cs for the full rationale).
        protected override NamingConfigResource GetResourceById(int id)
        {
            return GetNamingConfig().GetAwaiter().GetResult();
        }

        [HttpGet]
        public async Task<NamingConfigResource> GetNamingConfig()
        {
            var nameSpec = await _namingConfigService.GetConfig();
            var resource = nameSpec.ToResource();

            return resource;
        }

        [RestPutById]
        public async Task<ActionResult<NamingConfigResource>> UpdateNamingConfig([FromBody] NamingConfigResource resource)
        {
            var nameSpec = resource.ToModel();
            await ValidateFormatResult(nameSpec);

            await _namingConfigService.Save(nameSpec);

            return Accepted(resource.Id);
        }

        [HttpGet("examples")]
        public async Task<object> GetExamples([FromQuery]NamingConfigResource settings)
        {
            if (settings.Id == 0)
            {
                settings = await GetNamingConfig();
            }

            var nameSpec = settings.ToModel();
            var sampleResource = new NamingExampleResource();

            var singleEpisodeSampleResult = await _filenameSampleService.GetStandardSample(nameSpec);
            var multiEpisodeSampleResult = await _filenameSampleService.GetMultiEpisodeSample(nameSpec);
            var dailyEpisodeSampleResult = await _filenameSampleService.GetDailySample(nameSpec);
            var animeEpisodeSampleResult = await _filenameSampleService.GetAnimeSample(nameSpec);
            var animeMultiEpisodeSampleResult = await _filenameSampleService.GetAnimeMultiEpisodeSample(nameSpec);

            sampleResource.SingleEpisodeExample = _filenameValidationService.ValidateStandardFilename(singleEpisodeSampleResult) != null
                    ? null
                    : singleEpisodeSampleResult.FileName;

            sampleResource.MultiEpisodeExample = _filenameValidationService.ValidateStandardFilename(multiEpisodeSampleResult) != null
                    ? null
                    : multiEpisodeSampleResult.FileName;

            sampleResource.DailyEpisodeExample = _filenameValidationService.ValidateDailyFilename(dailyEpisodeSampleResult) != null
                    ? null
                    : dailyEpisodeSampleResult.FileName;

            sampleResource.AnimeEpisodeExample = _filenameValidationService.ValidateAnimeFilename(animeEpisodeSampleResult) != null
                    ? null
                    : animeEpisodeSampleResult.FileName;

            sampleResource.AnimeMultiEpisodeExample = _filenameValidationService.ValidateAnimeFilename(animeMultiEpisodeSampleResult) != null
                    ? null
                    : animeMultiEpisodeSampleResult.FileName;

            sampleResource.SeriesFolderExample = nameSpec.SeriesFolderFormat.IsNullOrWhiteSpace()
                ? null
                : await _filenameSampleService.GetSeriesFolderSample(nameSpec);

            sampleResource.SeasonFolderExample = nameSpec.SeasonFolderFormat.IsNullOrWhiteSpace()
                ? null
                : await _filenameSampleService.GetSeasonFolderSample(nameSpec);

            sampleResource.SpecialsFolderExample = nameSpec.SpecialsFolderFormat.IsNullOrWhiteSpace()
                ? null
                : await _filenameSampleService.GetSpecialsFolderSample(nameSpec);

            return sampleResource;
        }

        private async Task ValidateFormatResult(NamingConfig nameSpec)
        {
            var singleEpisodeSampleResult = await _filenameSampleService.GetStandardSample(nameSpec);
            var multiEpisodeSampleResult = await _filenameSampleService.GetMultiEpisodeSample(nameSpec);
            var dailyEpisodeSampleResult = await _filenameSampleService.GetDailySample(nameSpec);
            var animeEpisodeSampleResult = await _filenameSampleService.GetAnimeSample(nameSpec);
            var animeMultiEpisodeSampleResult = await _filenameSampleService.GetAnimeMultiEpisodeSample(nameSpec);

            var singleEpisodeValidationResult = _filenameValidationService.ValidateStandardFilename(singleEpisodeSampleResult);
            var multiEpisodeValidationResult = _filenameValidationService.ValidateStandardFilename(multiEpisodeSampleResult);
            var dailyEpisodeValidationResult = _filenameValidationService.ValidateDailyFilename(dailyEpisodeSampleResult);
            var animeEpisodeValidationResult = _filenameValidationService.ValidateAnimeFilename(animeEpisodeSampleResult);
            var animeMultiEpisodeValidationResult = _filenameValidationService.ValidateAnimeFilename(animeMultiEpisodeSampleResult);

            var validationFailures = new List<ValidationFailure>();

            validationFailures.AddIfNotNull(singleEpisodeValidationResult);
            validationFailures.AddIfNotNull(multiEpisodeValidationResult);
            validationFailures.AddIfNotNull(dailyEpisodeValidationResult);
            validationFailures.AddIfNotNull(animeEpisodeValidationResult);
            validationFailures.AddIfNotNull(animeMultiEpisodeValidationResult);

            if (validationFailures.Any())
            {
                throw new ValidationException(validationFailures.DistinctBy(v => v.PropertyName).ToArray());
            }
        }
    }
}
