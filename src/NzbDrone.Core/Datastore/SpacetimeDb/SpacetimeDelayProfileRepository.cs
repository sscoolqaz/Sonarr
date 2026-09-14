using System;
using System.Collections.Generic;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Delay;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbDelayProfile = SpacetimeDB.Types.DelayProfile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDelayProfileRepository : SpacetimeBasicRepository<DelayProfile, StdbDelayProfile>, IDelayProfileRepository
    {
        public SpacetimeDelayProfileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbDelayProfile> Table => Conn.Connection.Db.DelayProfile;

        protected override StdbDelayProfile FindRowById(int id) => Conn.Connection.Db.DelayProfile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, bool p2, bool p3, int p4, int p5, int p6, int p7, bool p8, bool p9, int p10, string p11)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnUpdateDelayProfile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateDelayProfile -= Handler);
        }

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

        public override void MigrateInsert(DelayProfile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertDelayProfile(
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
            SpacetimeJson.Serialize(model.Tags)));

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
