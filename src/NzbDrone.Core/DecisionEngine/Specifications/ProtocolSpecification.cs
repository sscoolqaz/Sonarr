using NLog;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Delay;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class ProtocolSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly IDelayProfileService _delayProfileService;
        private readonly Logger _logger;

        public ProtocolSpecification(IDelayProfileService delayProfileService,
                                     Logger logger)
        {
            _delayProfileService = delayProfileService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            // IDownloadDecisionEngineSpecification.IsSatisfiedBy stays sync (widely-shared, 30+ implementers -
            // see DownloadDecisionMaker report notes). Bridging is safe: runs off the request thread and
            // ASP.NET Core carries no SynchronizationContext, so this can't deadlock.
            var delayProfile = _delayProfileService.BestForTags(subject.Series.Tags).GetAwaiter().GetResult();

            if (subject.Release.DownloadProtocol == DownloadProtocol.Usenet && !delayProfile.EnableUsenet)
            {
                _logger.Debug("[{0}] Usenet is not enabled for this series", subject.Release.Title);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ProtocolDisabled, "Usenet is not enabled for this series");
            }

            if (subject.Release.DownloadProtocol == DownloadProtocol.Torrent && !delayProfile.EnableTorrent)
            {
                _logger.Debug("[{0}] Torrent is not enabled for this series", subject.Release.Title);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.ProtocolDisabled, "Torrent is not enabled for this series");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
