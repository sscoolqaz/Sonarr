using System;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.BroadcastheNet
{
    public class BroadcastheNet : HttpIndexerBase<BroadcastheNetSettings>
    {
        public override string Name => "BroadcasTheNet";

        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override bool SupportsRss => true;
        public override bool SupportsSearch => true;
        public override int PageSize => 100;
        public override TimeSpan RateLimit => TimeSpan.FromSeconds(5);

        public BroadcastheNet(IHttpClient httpClient, IIndexerStatusService indexerStatusService, IConfigService configService, IParsingService parsingService, Logger logger, ILocalizationService localizationService)
            : base(httpClient, indexerStatusService, configService, parsingService, logger, localizationService)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            var requestGenerator = new BroadcastheNetRequestGenerator { Settings = Settings, PageSize = PageSize };

            // GetRequestGenerator() is a synchronous abstract method on HttpIndexerBase shared by 11
            // concrete indexer implementations app-wide, only this one in this pass's scope - bridging
            // here avoids rippling a breaking signature change into the other 10. Safe: runs off the
            // request thread, no SynchronizationContext to deadlock against.
            var releaseInfo = _indexerStatusService.GetLastRssSyncReleaseInfo(Definition.Id).GetAwaiter().GetResult();

            if (releaseInfo != null && int.TryParse(releaseInfo.Guid.Replace("BTN-", string.Empty), out var torrentId))
            {
                requestGenerator.LastRecentTorrentId = torrentId;
            }

            return requestGenerator;
        }

        public override IParseIndexerResponse GetParser()
        {
            return new BroadcastheNetParser();
        }
    }
}
