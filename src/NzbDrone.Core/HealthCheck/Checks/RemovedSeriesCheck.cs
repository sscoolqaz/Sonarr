using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(SeriesUpdatedEvent))]
    [CheckOn(typeof(SeriesDeletedEvent))]
    [CheckOn(typeof(SeriesRefreshCompleteEvent))]
    public class RemovedSeriesCheck : HealthCheckBase, ICheckOnCondition<SeriesUpdatedEvent>, ICheckOnCondition<SeriesDeletedEvent>
    {
        private readonly ISeriesService _seriesService;

        public RemovedSeriesCheck(ISeriesService seriesService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _seriesService = seriesService;
        }

        // IProvideHealthCheck.Check() is a shared, synchronous interface with 27 implementers app-wide - its
        // signature can't change here. Bridging with GetAwaiter().GetResult() is safe for the same reason as
        // the IHandle bridges elsewhere this session (no SynchronizationContext on the threads this runs on).
        public override HealthCheck Check()
        {
            var allSeries = _seriesService.GetAllSeries().GetAwaiter().GetResult();
            var deletedSeries = allSeries.Where(v => v.Status == SeriesStatusType.Deleted).ToList();

            if (deletedSeries.Empty())
            {
                return new HealthCheck(GetType());
            }

            var seriesText = deletedSeries.Select(s => $"{s.Title} (tvdbid {s.TvdbId})").Join(", ");

            if (deletedSeries.Count == 1)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Error,
                    HealthCheckReason.RemovedSeriesSingle,
                    _localizationService.GetLocalizedString("RemovedSeriesSingleRemovedHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "series", seriesText }
                    }),
                    "#series-removed-from-thetvdb");
            }

            return new HealthCheck(GetType(),
                HealthCheckResult.Error,
                HealthCheckReason.RemovedSeriesMultiple,
                _localizationService.GetLocalizedString("RemovedSeriesMultipleRemovedHealthCheckMessage", new Dictionary<string, object>
                {
                    { "series", seriesText }
                }),
                "#series-removed-from-thetvdb");
        }

        public bool ShouldCheckOnEvent(SeriesDeletedEvent deletedEvent)
        {
            return deletedEvent.Series.Any(s => s.Status == SeriesStatusType.Deleted);
        }

        public bool ShouldCheckOnEvent(SeriesUpdatedEvent updatedEvent)
        {
            return updatedEvent.Series.Status == SeriesStatusType.Deleted;
        }
    }
}
