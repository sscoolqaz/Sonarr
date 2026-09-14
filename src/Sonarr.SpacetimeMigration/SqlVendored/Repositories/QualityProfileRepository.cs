using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Profiles.Qualities
{
    public class QualityProfileRepository : BasicRepository<QualityProfile>, IQualityProfileRepository
    {
        private readonly ICustomFormatService _customFormatService;

        public QualityProfileRepository(IMainDatabase database,
                                        IEventAggregator eventAggregator,
                                        ICustomFormatService customFormatService)
            : base(database, eventAggregator)
        {
            _customFormatService = customFormatService;
        }

        protected override List<QualityProfile> Query(SqlBuilder builder)
        {
            var cfs = _customFormatService.All().GetAwaiter().GetResult().ToDictionary(c => c.Id);

            var profiles = base.Query(builder);

            // Do the conversions from Id to full CustomFormat object here instead of in
            // CustomFormatIntConverter to remove need to for a static property containing
            // all the custom formats
            foreach (var profile in profiles)
            {
                var formatItems = new List<ProfileFormatItem>();

                foreach (var formatItem in profile.FormatItems)
                {
                    // Skip any format that has been removed, but the profile wasn't updated properly
                    if (cfs.ContainsKey(formatItem.Format.Id))
                    {
                        formatItem.Format = cfs[formatItem.Format.Id];

                        formatItems.Add(formatItem);
                    }
                }

                profile.FormatItems = formatItems;
            }

            return profiles;
        }

        public Task<bool> Exists(int id)
        {
            return Task.FromResult(Query(p => p.Id == id).Count == 1);
        }
    }
}
