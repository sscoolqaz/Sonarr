using System.Collections.Generic;
using System.IO;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators
{
    public class AggregateEpisodes : IAggregateLocalEpisode
    {
        public int Order => 1;

        private readonly IParsingService _parsingService;

        public AggregateEpisodes(IParsingService parsingService)
        {
            _parsingService = parsingService;
        }

        public LocalEpisode Aggregate(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            localEpisode.Episodes = GetEpisodes(localEpisode);

            return localEpisode;
        }

        private ParsedEpisodeInfo GetBestEpisodeInfo(LocalEpisode localEpisode)
        {
            var parsedEpisodeInfo = localEpisode.FileEpisodeInfo;
            var downloadClientEpisodeInfo = localEpisode.DownloadClientEpisodeInfo;
            var folderEpisodeInfo = localEpisode.FolderEpisodeInfo;

            if (!localEpisode.OtherVideoFiles && !SceneChecker.IsSceneTitle(Path.GetFileNameWithoutExtension(localEpisode.Path)))
            {
                if (downloadClientEpisodeInfo != null &&
                    !downloadClientEpisodeInfo.FullSeason &&
                    PreferOtherEpisodeInfo(parsedEpisodeInfo, downloadClientEpisodeInfo))
                {
                    parsedEpisodeInfo = localEpisode.DownloadClientEpisodeInfo;
                }
                else if (folderEpisodeInfo != null &&
                         !folderEpisodeInfo.FullSeason &&
                         PreferOtherEpisodeInfo(parsedEpisodeInfo, folderEpisodeInfo))
                {
                    parsedEpisodeInfo = localEpisode.FolderEpisodeInfo;
                }
            }

            if (parsedEpisodeInfo == null)
            {
                parsedEpisodeInfo = GetSpecialEpisodeInfo(localEpisode, parsedEpisodeInfo);
            }

            return parsedEpisodeInfo;
        }

        // IAggregateLocalEpisode is a shared interface with 9 implementers app-wide (most out of scope this
        // round) invoked synchronously from IAggregationService.Augment, which is also out of scope. Bridging
        // with GetAwaiter().GetResult() is safe for the same reason as the IHandle bridges elsewhere this
        // session.
        private ParsedEpisodeInfo GetSpecialEpisodeInfo(LocalEpisode localEpisode, ParsedEpisodeInfo parsedEpisodeInfo)
        {
            var title = Path.GetFileNameWithoutExtension(localEpisode.Path);
            var specialEpisodeInfo = _parsingService.ParseSpecialEpisodeTitle(parsedEpisodeInfo, title, localEpisode.Series).GetAwaiter().GetResult();

            return specialEpisodeInfo;
        }

        private List<Episode> GetEpisodes(LocalEpisode localEpisode)
        {
            var bestEpisodeInfoForEpisodes = GetBestEpisodeInfo(localEpisode);
            var isMediaFile = MediaFileExtensions.Extensions.Contains(Path.GetExtension(localEpisode.Path));

            if (bestEpisodeInfoForEpisodes == null)
            {
                return new List<Episode>();
            }

            if (ValidateParsedEpisodeInfo.ValidateForSeriesType(bestEpisodeInfoForEpisodes, localEpisode.Series, isMediaFile))
            {
                var episodes = _parsingService.GetEpisodes(bestEpisodeInfoForEpisodes, localEpisode.Series, localEpisode.SceneSource).GetAwaiter().GetResult();

                if (episodes.Empty() && bestEpisodeInfoForEpisodes.IsPossibleSpecialEpisode)
                {
                    var parsedSpecialEpisodeInfo = GetSpecialEpisodeInfo(localEpisode, bestEpisodeInfoForEpisodes);

                    if (parsedSpecialEpisodeInfo != null)
                    {
                        episodes = _parsingService.GetEpisodes(parsedSpecialEpisodeInfo, localEpisode.Series, localEpisode.SceneSource).GetAwaiter().GetResult();
                    }
                }

                return episodes;
            }

            return new List<Episode>();
        }

        private bool PreferOtherEpisodeInfo(ParsedEpisodeInfo fileEpisodeInfo, ParsedEpisodeInfo otherEpisodeInfo)
        {
            if (fileEpisodeInfo == null)
            {
                return true;
            }

            // When the files episode info is not absolute prefer it over a parsed episode info that is absolute
            if (!fileEpisodeInfo.IsAbsoluteNumbering && otherEpisodeInfo.IsAbsoluteNumbering)
            {
                return false;
            }

            return true;
        }
    }
}
