using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using Sonarr.Http;
using Sonarr.Http.REST;

namespace Sonarr.Api.V5.Release;

[V5ApiController("release/push")]
public class ReleasePushController : RestController<ReleasePushResource>
{
    private readonly IMakeDownloadDecision _downloadDecisionMaker;
    private readonly IProcessDownloadDecisions _downloadDecisionProcessor;
    private readonly IIndexerFactory _indexerFactory;
    private readonly IDownloadClientFactory _downloadClientFactory;
    private readonly Logger _logger;

    private readonly QualityProfile _qualityProfile;

    // NOTE: the original synchronous `lock` can't wrap an `await` (CS1996), and the
    // decision-making/processing calls below are genuinely async now, so a SemaphoreSlim
    // replaces the lock to serialize pushes without blocking the request thread (see
    // V3's Indexers/ReleasePushController.cs for the same pattern).
    private static readonly SemaphoreSlim PushLock = new SemaphoreSlim(1, 1);

    public ReleasePushController(IMakeDownloadDecision downloadDecisionMaker,
                             IProcessDownloadDecisions downloadDecisionProcessor,
                             IIndexerFactory indexerFactory,
                             IDownloadClientFactory downloadClientFactory,
                             IQualityProfileService qualityProfileService,
                             Logger logger)
    {
        _downloadDecisionMaker = downloadDecisionMaker;
        _downloadDecisionProcessor = downloadDecisionProcessor;
        _indexerFactory = indexerFactory;
        _downloadClientFactory = downloadClientFactory;
        _logger = logger;

        // NOTE: C# constructors cannot be async, and this controller is instantiated by DI per
        // request, so there's no async-all-the-way path here; blocking via GetAwaiter().GetResult()
        // is the documented boundary (see ProviderControllerBase.cs for the same rationale).
        _qualityProfile = qualityProfileService.GetDefaultProfile(string.Empty).GetAwaiter().GetResult();

        PostValidator.RuleFor(s => s.Title).NotEmpty();
        PostValidator.RuleFor(s => s.DownloadUrl).NotEmpty().When(s => s.MagnetUrl.IsNullOrWhiteSpace());
        PostValidator.RuleFor(s => s.MagnetUrl).NotEmpty().When(s => s.DownloadUrl.IsNullOrWhiteSpace());
        PostValidator.RuleFor(s => s.Protocol).NotEmpty();
        PostValidator.RuleFor(s => s.PublishDate).NotEmpty();
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<Results<Ok<ReleaseResource>, BadRequest>> Create([FromBody] ReleasePushResource release)
    {
        _logger.Info("Release pushed: {0} - {1}", release.Title, release.DownloadUrl ?? release.MagnetUrl);

        ValidateResource(release);

        var info = release.ToModel();

        info.Guid = "PUSH-" + info.DownloadUrl;

        await ResolveIndexer(info);

        var downloadClientId = await ResolveDownloadClientId(release);

        DownloadDecision? decision;

        await PushLock.WaitAsync();

        try
        {
            var decisions = await _downloadDecisionMaker.GetRssDecision(new List<ReleaseInfo> { info }, true);

            decision = decisions.FirstOrDefault();

            await _downloadDecisionProcessor.ProcessDecision(decision, downloadClientId);
        }
        finally
        {
            PushLock.Release();
        }

        if (decision?.RemoteEpisode.ParsedEpisodeInfo == null)
        {
            throw new ValidationException(new List<ValidationFailure> { new("Title", "Unable to parse", release.Title) });
        }

        return TypedResults.Ok(decision.MapDecision(1, _qualityProfile));
    }

    private async Task ResolveIndexer(ReleaseInfo release)
    {
        var indexer = await _indexerFactory.ResolveIndexer(release.IndexerId, release.Indexer);

        if (indexer == null)
        {
            _logger.Debug("Push Release {0} not associated with an indexer.", release.Title);
        }
        else
        {
            _logger.Debug("Push Release {0} associated with indexer '{1} ({2})", release.Title, indexer.Name, indexer.Id);

            release.IndexerId = indexer.Id;
            release.Indexer = indexer.Name;
        }
    }

    private async Task<int?> ResolveDownloadClientId(ReleasePushResource release)
    {
        var downloadClient = await _downloadClientFactory.ResolveDownloadClient(release.DownloadClientId, release.DownloadClientName);

        if (downloadClient == null)
        {
            _logger.Debug("Push Release {0} not associated with a download client.", release.Title);
        }
        else
        {
            _logger.Debug("Push Release {0} associated with download client '{1} ({2})", release.Title, downloadClient.Name, downloadClient.Id);
        }

        return downloadClient?.Id;
    }
}
