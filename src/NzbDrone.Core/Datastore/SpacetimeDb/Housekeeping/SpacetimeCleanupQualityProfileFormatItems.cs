using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real CleanupQualityProfileFormatItems always operates through its own dedicated
    // IQualityProfileFormatItemsCleanupRepository (a bespoke BasicRepository<QualityProfile>
    // subclass declared directly in that file, distinct from the normal swappable
    // IQualityProfileRepository) - so unlike CleanupCommandQueue/CleanupExtraFilesInExcludedFolders/
    // DeleteBadMediaCovers/UpdateCleanTitleForSeries/the four FixFuture*StatusTimes tasks (which
    // all go through an interface this port's DI fix already redirects correctly), this one
    // always reads/writes the real SQL QualityProfiles table regardless of SpacetimeDb mode - a
    // no-op once SpacetimeDB is the write path. Reimplemented here against the normal
    // IQualityProfileRepository/ICustomFormatRepository instead, replicating the real logic
    // exactly (same add-missing/remove-stale format item reconciliation, same
    // MinFormatScore/CutoffFormatScore/MinUpgradeFormatScore reset when a profile ends up with no
    // format items at all).
    //
    // Depends on the concrete SpacetimeQualityProfileRepository (not IQualityProfileRepository)
    // for GetRawFormatItemIds(): that repository's own ToModel silently drops any stored format
    // item whose CustomFormat id no longer exists, so profile.FormatItems (as returned by
    // IQualityProfileRepository.All()) is already stale-free and can never reveal a removal for
    // the diff below to detect - the raw ids are the only way to see what's actually still
    // persisted.
    public class SpacetimeCleanupQualityProfileFormatItems : IHousekeepingTask
    {
        private readonly SpacetimeQualityProfileRepository _qualityProfileRepository;
        private readonly ICustomFormatRepository _customFormatRepository;

        public SpacetimeCleanupQualityProfileFormatItems(SpacetimeQualityProfileRepository qualityProfileRepository, ICustomFormatRepository customFormatRepository)
        {
            _qualityProfileRepository = qualityProfileRepository;
            _customFormatRepository = customFormatRepository;
        }

        public async Task Clean()
        {
            var customFormats = (await _customFormatRepository.All()).ToDictionary(c => c.Id);
            var profiles = await _qualityProfileRepository.All();
            var rawFormatIds = await _qualityProfileRepository.GetRawFormatItemIds();
            var updatedProfiles = new List<QualityProfile>();

            foreach (var profile in profiles)
            {
                var formatItems = new List<ProfileFormatItem>();

                profile.FormatItems.ForEach(p =>
                {
                    if (p.Format != null && customFormats.ContainsKey(p.Format.Id))
                    {
                        formatItems.Add(p);
                    }
                });

                foreach (var customFormat in customFormats)
                {
                    if (formatItems.None(f => f.Format.Id == customFormat.Key))
                    {
                        formatItems.Insert(0, new ProfileFormatItem
                        {
                            Format = customFormat.Value,
                            Score = 0
                        });
                    }
                }

                var previousIds = rawFormatIds.TryGetValue(profile.Id, out var rawIds) ? rawIds : new List<int>();
                var ids = formatItems.Select(i => i.Format.Id).ToList();

                if (ids.Except(previousIds).Any() || previousIds.Except(ids).Any())
                {
                    profile.FormatItems = formatItems;

                    if (profile.FormatItems.Empty())
                    {
                        profile.MinFormatScore = 0;
                        profile.CutoffFormatScore = 0;
                        profile.MinUpgradeFormatScore = 1;
                    }

                    updatedProfiles.Add(profile);
                }
            }

            if (updatedProfiles.Any())
            {
                await _qualityProfileRepository.SetFields(updatedProfiles, p => p.FormatItems, p => p.MinFormatScore, p => p.CutoffFormatScore, p => p.MinUpgradeFormatScore);
            }
        }
    }
}
