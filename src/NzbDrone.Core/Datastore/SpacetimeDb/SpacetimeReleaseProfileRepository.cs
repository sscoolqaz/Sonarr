using System;
using System.Collections.Generic;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Releases;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbReleaseProfile = SpacetimeDB.Types.ReleaseProfile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeReleaseProfileRepository : SpacetimeBasicRepository<ReleaseProfile, StdbReleaseProfile>, IRestrictionRepository
    {
        public SpacetimeReleaseProfileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbReleaseProfile> Table => Conn.Connection.Db.ReleaseProfile;

        protected override StdbReleaseProfile FindRowById(int id) => Conn.Connection.Db.ReleaseProfile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, bool p3, string p4, string p5, bool p6, int p7, bool p8, string p9, string p10, string p11)
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

            Conn.Connection.Reducers.OnUpdateReleaseProfile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateReleaseProfile -= Handler);
        }

        protected override ReleaseProfile ToModel(StdbReleaseProfile row) => new ReleaseProfile
        {
            Id = row.Id,
            Name = row.Name,
            Enabled = row.Enabled,
            Required = SpacetimeJson.Deserialize<List<string>>(row.RequiredJson) ?? new List<string>(),
            Ignored = SpacetimeJson.Deserialize<List<string>>(row.IgnoredJson) ?? new List<string>(),
            AirDateRestriction = row.AirDateRestriction,
            AirDateGracePeriod = row.AirDateGracePeriod,
            AllowSeasonPackWithoutAllEpisodesAired = row.AllowSeasonPackWithoutAllEpisodesAired,
            IndexerIds = SpacetimeJson.Deserialize<List<int>>(row.IndexerIdsJson) ?? new List<int>(),
            Tags = SpacetimeJson.Deserialize<HashSet<int>>(row.TagsJson) ?? new HashSet<int>(),
            ExcludedTags = SpacetimeJson.Deserialize<HashSet<int>>(row.ExcludedTagsJson) ?? new HashSet<int>()
        };

        protected override int GetRowId(StdbReleaseProfile row) => row.Id;

        protected override void InvokeInsertReducer(ReleaseProfile model) => Conn.Connection.Reducers.InsertReleaseProfile(
            model.Name ?? string.Empty,
            model.Enabled,
            SpacetimeJson.Serialize(model.Required),
            SpacetimeJson.Serialize(model.Ignored),
            model.AirDateRestriction,
            model.AirDateGracePeriod,
            model.AllowSeasonPackWithoutAllEpisodesAired,
            SpacetimeJson.Serialize(model.IndexerIds),
            SpacetimeJson.Serialize(model.Tags),
            SpacetimeJson.Serialize(model.ExcludedTags));

        public override void MigrateInsert(ReleaseProfile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertReleaseProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.Enabled,
            SpacetimeJson.Serialize(model.Required),
            SpacetimeJson.Serialize(model.Ignored),
            model.AirDateRestriction,
            model.AirDateGracePeriod,
            model.AllowSeasonPackWithoutAllEpisodesAired,
            SpacetimeJson.Serialize(model.IndexerIds),
            SpacetimeJson.Serialize(model.Tags),
            SpacetimeJson.Serialize(model.ExcludedTags)));

        protected override void InvokeUpdateReducer(ReleaseProfile model) => Conn.Connection.Reducers.UpdateReleaseProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.Enabled,
            SpacetimeJson.Serialize(model.Required),
            SpacetimeJson.Serialize(model.Ignored),
            model.AirDateRestriction,
            model.AirDateGracePeriod,
            model.AllowSeasonPackWithoutAllEpisodesAired,
            SpacetimeJson.Serialize(model.IndexerIds),
            SpacetimeJson.Serialize(model.Tags),
            SpacetimeJson.Serialize(model.ExcludedTags));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteReleaseProfile(id);
    }
}
