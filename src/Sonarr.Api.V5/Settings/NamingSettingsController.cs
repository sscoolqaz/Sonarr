using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using Sonarr.Http;
using Sonarr.Http.REST;
using Sonarr.Http.REST.Attributes;

namespace Sonarr.Api.V5.Settings;

[V5ApiController("settings/naming")]
public class NamingSettingsController : RestController<NamingSettingsResource>
{
    private readonly INamingConfigService _namingConfigService;
    private readonly IFilenameSampleService _filenameSampleService;
    private readonly IFilenameValidationService _filenameValidationService;

    public NamingSettingsController(INamingConfigService namingConfigService,
                              IFilenameSampleService filenameSampleService,
                              IFilenameValidationService filenameValidationService)
    {
        _namingConfigService = namingConfigService;
        _filenameSampleService = filenameSampleService;
        _filenameValidationService = filenameValidationService;

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
    // app-wide (see ProviderControllerBase.cs for the full rationale); blocking here via
    // GetAwaiter().GetResult() is the documented boundary rather than converting that shared
    // base class.
    protected override NamingSettingsResource GetResourceById(int id)
    {
        return _namingConfigService.GetConfig().GetAwaiter().GetResult().ToResource();
    }

    private async Task<NamingSettingsResource> GetResourceByIdAsync(int id)
    {
        return (await _namingConfigService.GetConfig()).ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public async Task<Ok<NamingSettingsResource>> GetNamingConfig()
    {
        return TypedResults.Ok(await GetResourceByIdAsync(1));
    }

    [RestPutById]
    [Consumes("application/json")]
    public async Task<Results<Accepted<NamingSettingsResource>, NotFound>> UpdateNamingConfig([FromBody] NamingSettingsResource resource)
    {
        var nameSpec = resource.ToModel();
        await ValidateFormatResult(nameSpec);

        await _namingConfigService.Save(nameSpec);

        return TypedAccepted(resource.Id);
    }

    [HttpGet("examples")]
    [Produces("application/json")]
    public async Task<Ok<NamingExampleResource>> GetExamples([FromQuery] NamingSettingsResource settings)
    {
        if (settings.Id == 0)
        {
            settings = await GetResourceByIdAsync(1);
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

        return TypedResults.Ok(sampleResource);
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
