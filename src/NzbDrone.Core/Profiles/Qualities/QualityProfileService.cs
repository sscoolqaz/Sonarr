using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileService
    {
        Task<QualityProfile> Add(QualityProfile profile);
        Task Update(QualityProfile profile);
        Task Delete(int id);
        Task<List<QualityProfile>> All();
        Task<QualityProfile> Get(int id);
        Task<bool> Exists(int id);
        Task<QualityProfile> GetDefaultProfile(string name, Quality cutoff = null, params Quality[] allowed);
        Task UpdateAllSizeLimits(params QualityProfileSizeLimit[] sizeLimits);
    }

    public class QualityProfileService : IQualityProfileService,
                                         IHandle<ApplicationStartedEvent>,
                                         IHandle<CustomFormatAddedEvent>,
                                         IHandle<CustomFormatDeletedEvent>
    {
        private readonly IQualityProfileRepository _qualityProfileRepository;
        private readonly IImportListFactory _importListFactory;
        private readonly ICustomFormatService _formatService;
        private readonly ISeriesService _seriesService;
        private readonly IQualityProfileRankService _rankService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public QualityProfileService(IQualityProfileRepository qualityProfileRepository,
                                     IImportListFactory importListFactory,
                                     ICustomFormatService formatService,
                                     ISeriesService seriesService,
                                     IQualityProfileRankService rankService,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
        {
            _qualityProfileRepository = qualityProfileRepository;
            _importListFactory = importListFactory;
            _formatService = formatService;
            _seriesService = seriesService;
            _rankService = rankService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public async Task<QualityProfile> Add(QualityProfile profile)
        {
            var saved = await _qualityProfileRepository.Insert(profile);
            await _rankService.UpdateRanksForProfile(saved);
            return saved;
        }

        public async Task Update(QualityProfile profile)
        {
            await _qualityProfileRepository.Update(profile);
            await _rankService.UpdateRanksForProfile(profile);
            _eventAggregator.PublishEvent(new QualityProfileUpdatedEvent(profile.Id));
        }

        public async Task Delete(int id)
        {
            if ((await _seriesService.GetAllSeries()).Any(c => c.QualityProfileId == id) || (await _importListFactory.All()).Any(c => c.QualityProfileId == id))
            {
                var profile = await _qualityProfileRepository.Get(id);
                throw new QualityProfileInUseException(profile.Name);
            }

            await _qualityProfileRepository.Delete(id);
            await _rankService.DeleteRanksForProfile(id);
        }

        public async Task<List<QualityProfile>> All()
        {
            return (await _qualityProfileRepository.All()).ToList();
        }

        public async Task<QualityProfile> Get(int id)
        {
            return await _qualityProfileRepository.Get(id);
        }

        public async Task<bool> Exists(int id)
        {
            return await _qualityProfileRepository.Exists(id);
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide); its
        // `void Handle(TEvent message)` signature is out of scope to convert (see architectural
        // note in ProviderFactory.cs). Blocking here via GetAwaiter().GetResult() on the private
        // async body is the documented boundary.
        public void Handle(ApplicationStartedEvent message)
        {
            HandleApplicationStarted().GetAwaiter().GetResult();
        }

        private async Task HandleApplicationStarted()
        {
            var profiles = await All();

            if (profiles.Any())
            {
                await _rankService.SeedAll(profiles);

                return;
            }

            _logger.Info("Setting up default quality profiles");

            await AddDefaultProfile("Any",
                Quality.SDTV,
                Quality.SDTV,
                Quality.WEBRip480p,
                Quality.WEBDL480p,
                Quality.DVD,
                Quality.Bluray480p,
                Quality.Bluray576p,
                Quality.HDTV720p,
                Quality.HDTV1080p,
                Quality.WEBRip720p,
                Quality.WEBDL720p,
                Quality.WEBRip1080p,
                Quality.WEBDL1080p,
                Quality.Bluray720p,
                Quality.Bluray1080p);

            await AddDefaultProfile("SD",
                Quality.SDTV,
                Quality.SDTV,
                Quality.WEBRip480p,
                Quality.WEBDL480p,
                Quality.DVD,
                Quality.Bluray480p,
                Quality.Bluray576p);

            await AddDefaultProfile("HD-720p",
                Quality.HDTV720p,
                Quality.HDTV720p,
                Quality.WEBRip720p,
                Quality.WEBDL720p,
                Quality.Bluray720p);

            await AddDefaultProfile("HD-1080p",
                Quality.HDTV1080p,
                Quality.HDTV1080p,
                Quality.WEBRip1080p,
                Quality.WEBDL1080p,
                Quality.Bluray1080p);

            await AddDefaultProfile("Ultra-HD",
                Quality.HDTV2160p,
                Quality.HDTV2160p,
                Quality.WEBRip2160p,
                Quality.WEBDL2160p,
                Quality.Bluray2160p);

            await AddDefaultProfile("HD - 720p/1080p",
                Quality.HDTV720p,
                Quality.HDTV720p,
                Quality.HDTV1080p,
                Quality.WEBRip720p,
                Quality.WEBDL720p,
                Quality.WEBRip1080p,
                Quality.WEBDL1080p,
                Quality.Bluray720p,
                Quality.Bluray1080p);
        }

        public void Handle(CustomFormatAddedEvent message)
        {
            HandleCustomFormatAdded(message).GetAwaiter().GetResult();
        }

        private async Task HandleCustomFormatAdded(CustomFormatAddedEvent message)
        {
            var all = await All();

            foreach (var profile in all)
            {
                profile.FormatItems.Insert(0, new ProfileFormatItem
                {
                    Score = 0,
                    Format = message.CustomFormat
                });

                await Update(profile);
            }
        }

        public void Handle(CustomFormatDeletedEvent message)
        {
            HandleCustomFormatDeleted(message).GetAwaiter().GetResult();
        }

        private async Task HandleCustomFormatDeleted(CustomFormatDeletedEvent message)
        {
            var all = await All();
            foreach (var profile in all)
            {
                profile.FormatItems = profile.FormatItems.Where(c => c.Format.Id != message.CustomFormat.Id).ToList();

                if (profile.FormatItems.Empty())
                {
                    profile.MinFormatScore = 0;
                    profile.CutoffFormatScore = 0;
                    profile.MinUpgradeFormatScore = 1;
                }

                await Update(profile);
            }
        }

        public async Task<QualityProfile> GetDefaultProfile(string name, Quality cutoff = null, params Quality[] allowed)
        {
            var groupedQualites = Quality.DefaultQualityDefinitions.GroupBy(q => q.Weight);
            var items = new List<QualityProfileQualityItem>();
            var groupId = 1000;
            var profileCutoff = cutoff == null ? Quality.Unknown.Id : cutoff.Id;

            foreach (var group in groupedQualites)
            {
                if (group.Count() == 1)
                {
                    var quality = group.First().Quality;

                    items.Add(new QualityProfileQualityItem
                    {
                        Quality = group.First().Quality,
                        Allowed = allowed.Contains(quality),
                        MinSize = group.First().MinSize,
                        MaxSize = group.First().MaxSize,
                        PreferredSize = group.First().PreferredSize
                    });
                    continue;
                }

                var groupAllowed = group.Any(g => allowed.Contains(g.Quality));

                items.Add(new QualityProfileQualityItem
                {
                    Id = groupId,
                    Name = group.First().GroupName,
                    Items = group.Select(g => new QualityProfileQualityItem
                    {
                        Quality = g.Quality,
                        Allowed = groupAllowed,
                        MinSize = g.MinSize,
                        MaxSize = g.MaxSize,
                        PreferredSize = g.PreferredSize
                    }).ToList(),
                    Allowed = groupAllowed
                });

                if (group.Any(g => g.Quality.Id == profileCutoff))
                {
                    profileCutoff = groupId;
                }

                groupId++;
            }

            var formatItems = (await _formatService.All()).Select(format => new ProfileFormatItem
            {
                Score = 0,
                Format = format
            }).ToList();

            var qualityProfile = new QualityProfile
                                 {
                                     Name = name,
                                     Cutoff = profileCutoff,
                                     Items = items,
                                     MinFormatScore = 0,
                                     CutoffFormatScore = 0,
                                     MinUpgradeFormatScore = 1,
                                     FormatItems = formatItems
                                 };

            return qualityProfile;
        }

        public async Task UpdateAllSizeLimits(params QualityProfileSizeLimit[] sizeLimits)
        {
            var all = await All();

            foreach (var qualityProfile in all)
            {
                foreach (var sizeLimit in sizeLimits)
                {
                        var qualityIndex = qualityProfile.GetIndex(sizeLimit.Quality, true);
                        var qualityOrGroup = qualityProfile.Items[qualityIndex.Index];
                        var item = qualityOrGroup.Quality == null ? qualityOrGroup.Items[qualityIndex.GroupIndex] : qualityOrGroup;

                        item.MinSize = sizeLimit.MinSize;
                        item.MaxSize = sizeLimit.MaxSize;
                        item.PreferredSize = sizeLimit.PreferredSize;
                }
            }

            await _qualityProfileRepository.UpdateMany(all);
        }

        private async Task<QualityProfile> AddDefaultProfile(string name, Quality cutoff, params Quality[] allowed)
        {
            var profile = await GetDefaultProfile(name, cutoff, allowed);

            return await Add(profile);
        }
    }
}
