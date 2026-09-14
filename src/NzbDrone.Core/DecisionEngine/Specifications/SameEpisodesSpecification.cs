using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class SameEpisodesSpecification
    {
        private readonly IEpisodeService _episodeService;

        public SameEpisodesSpecification(IEpisodeService episodeService)
        {
            _episodeService = episodeService;
        }

        public bool IsSatisfiedBy(List<Episode> episodes)
        {
            var episodeIds = episodes.SelectList(e => e.Id);
            var episodeFileIds = episodes.Where(c => c.EpisodeFileId != 0).Select(c => c.EpisodeFileId).Distinct();

            foreach (var episodeFileId in episodeFileIds)
            {
                // Kept sync deliberately: this class is a shared helper consumed by both
                // SameEpisodesGrabSpecification (IDownloadDecisionEngineSpecification, sync by design -
                // see DownloadDecisionMaker report notes) and SameEpisodesImportSpecification
                // (IImportDecisionEngineSpecification, outside this pass's scope). Bridging the one
                // async repo call here avoids rippling an interface change into another agent's files.
                // Safe: runs off the request thread, no SynchronizationContext to deadlock against.
                var episodesInFile = _episodeService.GetEpisodesByFileId(episodeFileId).GetAwaiter().GetResult();

                if (episodesInFile.Select(e => e.Id).Except(episodeIds).Any())
                {
                    return false;
                }
            }

            return true;
        }
    }
}
