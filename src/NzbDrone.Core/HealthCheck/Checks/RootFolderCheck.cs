using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(SeriesDeletedEvent))]
    [CheckOn(typeof(SeriesMovedEvent))]
    [CheckOn(typeof(EpisodeImportedEvent), CheckOnCondition.FailedOnly)]
    [CheckOn(typeof(EpisodeImportFailedEvent), CheckOnCondition.SuccessfulOnly)]
    public class RootFolderCheck : HealthCheckBase
    {
        private readonly ISeriesService _seriesService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderService _rootFolderService;

        public RootFolderCheck(ISeriesService seriesService, IDiskProvider diskProvider, IRootFolderService rootFolderService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _seriesService = seriesService;
            _diskProvider = diskProvider;
            _rootFolderService = rootFolderService;
        }

        // IProvideHealthCheck.Check() is a shared, synchronous interface with 27 implementers app-wide - its
        // signature can't change here. Bridging with GetAwaiter().GetResult() is safe for the same reason as
        // the IHandle bridges elsewhere this session (no SynchronizationContext on the threads this runs on).
        public override HealthCheck Check()
        {
            var seriesPaths = _seriesService.GetAllSeriesPaths().GetAwaiter().GetResult();

            var rootFolders = new List<string>();

            foreach (var seriesPath in seriesPaths)
            {
                var rootFolderPath = _rootFolderService.GetBestRootFolderPath(seriesPath.Value).GetAwaiter().GetResult();

                if (!rootFolders.Contains(rootFolderPath))
                {
                    rootFolders.Add(rootFolderPath);
                }
            }

            var missingRootFolders = rootFolders.Where(s => !s.IsPathValid(PathValidationType.CurrentOs) || !_diskProvider.FolderExists(s))
                .ToList();

            if (missingRootFolders.Any())
            {
                if (missingRootFolders.Count == 1)
                {
                    return new HealthCheck(GetType(),
                        HealthCheckResult.Error,
                        HealthCheckReason.RootFolderMissing,
                        _localizationService.GetLocalizedString(
                            "RootFolderMissingHealthCheckMessage",
                            new Dictionary<string, object>
                            {
                                { "rootFolderPath", missingRootFolders.First() }
                            }),
                        "#missing-root-folder");
                }

                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.RootFolderMultipleMissing,
                    _localizationService.GetLocalizedString(
                        "RootFolderMultipleMissingHealthCheckMessage",
                        new Dictionary<string, object>
                        {
                            { "rootFolderPaths", string.Join(" | ", missingRootFolders) }
                        }),
                    "#missing-root-folder");
            }

            var emptyRootFolders = rootFolders
                .Where(r => _diskProvider.FolderEmpty(r))
                .ToList();

            if (emptyRootFolders.Any())
            {
                if (emptyRootFolders.Count == 1)
                {
                    return new HealthCheck(GetType(),
                        HealthCheckResult.Warning,
                        HealthCheckReason.RootFolderEmpty,
                        _localizationService.GetLocalizedString(
                            "RootFolderEmptyHealthCheckMessage",
                            new Dictionary<string, object>
                            {
                                { "rootFolderPath", emptyRootFolders.First() }
                            }),
                        "#empty-root-folder");
                }

                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.RootFolderEmpty,
                    _localizationService.GetLocalizedString(
                        "RootFolderMultipleEmptyHealthCheckMessage",
                        new Dictionary<string, object>
                        {
                            { "rootFolderPaths", string.Join(" | ", emptyRootFolders) }
                        }),
                    "#empty-root-folder");
            }

            return new HealthCheck(GetType());
        }
    }
}
