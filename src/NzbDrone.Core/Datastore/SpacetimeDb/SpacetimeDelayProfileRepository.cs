using System.Collections.Generic;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Delay;
using StdbDelayProfile = SpacetimeDB.Types.DelayProfile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDelayProfileRepository : SpacetimeBasicRepository<DelayProfile, StdbDelayProfile>, IDelayProfileRepository
    {
        public SpacetimeDelayProfileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbDelayProfile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.DelayProfile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override DelayProfile ToModel(StdbDelayProfile row) => new DelayProfile
        {
            Id = row.Id,
            EnableUsenet = row.EnableUsenet,
            EnableTorrent = row.EnableTorrent,
            PreferredProtocol = (DownloadProtocol)row.PreferredProtocol,
            UsenetDelay = row.UsenetDelay,
            TorrentDelay = row.TorrentDelay,
            Order = row.Order,
            BypassIfHighestQuality = row.BypassIfHighestQuality,
            BypassIfAboveCustomFormatScore = row.BypassIfAboveCustomFormatScore,
            MinimumCustomFormatScore = row.MinimumCustomFormatScore,
            Tags = SpacetimeJson.Deserialize<HashSet<int>>(row.TagsJson) ?? new HashSet<int>()
        };

        protected override int GetRowId(StdbDelayProfile row) => row.Id;

        protected override void InvokeInsertReducer(DelayProfile model) => Conn.Connection.Reducers.InsertDelayProfile(
            model.EnableUsenet,
            model.EnableTorrent,
            (int)model.PreferredProtocol,
            model.UsenetDelay,
            model.TorrentDelay,
            model.Order,
            model.BypassIfHighestQuality,
            model.BypassIfAboveCustomFormatScore,
            model.MinimumCustomFormatScore,
            SpacetimeJson.Serialize(model.Tags));

        protected override void InvokeUpdateReducer(DelayProfile model) => Conn.Connection.Reducers.UpdateDelayProfile(
            model.Id,
            model.EnableUsenet,
            model.EnableTorrent,
            (int)model.PreferredProtocol,
            model.UsenetDelay,
            model.TorrentDelay,
            model.Order,
            model.BypassIfHighestQuality,
            model.BypassIfAboveCustomFormatScore,
            model.MinimumCustomFormatScore,
            SpacetimeJson.Serialize(model.Tags));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteDelayProfile(id);

        protected override object GetSortKey(DelayProfile model) => model.Order;
    }
}
