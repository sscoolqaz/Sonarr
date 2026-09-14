using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Tv
{
    public interface IEpisodeCutoffService
    {
        PagingSpec<Episode> EpisodesWhereCutoffUnmet(PagingSpec<Episode> pagingSpec, HashSet<int> seriesTags = null, List<int> quality = null);
    }

    public class EpisodeCutoffService : IEpisodeCutoffService
    {
        private readonly IEpisodeRepository _episodeRepository;
        private readonly IQualityProfileService _qualityProfileService;

        public EpisodeCutoffService(IEpisodeRepository episodeRepository, IQualityProfileService qualityProfileService)
        {
            _episodeRepository = episodeRepository;
            _qualityProfileService = qualityProfileService;
        }

        public PagingSpec<Episode> EpisodesWhereCutoffUnmet(PagingSpec<Episode> pagingSpec, HashSet<int> seriesTags = null, List<int> quality = null)
        {
            // IEpisodeCutoffService is kept synchronous - its only caller (EpisodeSearchService)
            // already bridges around it. Bridge the now-async calls here instead.
            var qualitiesBelowCutoff = new List<QualitiesBelowCutoff>();
            var profiles = _qualityProfileService.All().GetAwaiter().GetResult();

            // Get all items less than the cutoff
            foreach (var profile in profiles)
            {
                var cutoff = profile.UpgradeAllowed ? profile.Cutoff : profile.FirststAllowedQuality().Id;
                var cutoffIndex = profile.GetIndex(cutoff);
                var belowCutoff = profile.Items.Take(cutoffIndex.Index).ToList();

                if (belowCutoff.Any())
                {
                    qualitiesBelowCutoff.Add(new QualitiesBelowCutoff(profile.Id, belowCutoff.SelectMany(i => i.GetQualities().Select(q => q.Id))));
                }
            }

            if (qualitiesBelowCutoff.Empty())
            {
                pagingSpec.Records = new List<Episode>();

                return pagingSpec;
            }

            return _episodeRepository.EpisodesWhereCutoffUnmet(pagingSpec, qualitiesBelowCutoff, false, seriesTags, quality).GetAwaiter().GetResult();
        }
    }
}
