using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Download.Pending;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbPendingRelease = SpacetimeDB.Types.PendingRelease;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimePendingReleaseRepository : SpacetimeBasicRepository<PendingRelease, StdbPendingRelease>, IPendingReleaseRepository
    {
        public SpacetimePendingReleaseRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbPendingRelease> Table => Conn.Connection.Db.PendingRelease;

        protected override StdbPendingRelease FindRowById(int id) => Conn.Connection.Db.PendingRelease.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, string p3, SpacetimeDB.Timestamp p4, string p5, string p6, int p7, string p8)
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

            Conn.Connection.Reducers.OnUpdatePendingRelease += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdatePendingRelease -= Handler);
        }

        protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id)
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

            Conn.Connection.Reducers.OnDeletePendingRelease += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeletePendingRelease -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int p1, string p2, SpacetimeDB.Timestamp p3, string p4, string p5, int p6, string p7)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted();
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnInsertPendingRelease += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertPendingRelease -= Handler);
        }

        protected override PendingRelease ToModel(StdbPendingRelease row) => new PendingRelease
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            Title = row.Title,
            Added = SpacetimeDateTime.ToDateTime(row.Added),
            ParsedEpisodeInfo = SpacetimeJson.Deserialize<ParsedEpisodeInfo>(row.ParsedEpisodeInfoJson),
            Release = SpacetimeJson.Deserialize<ReleaseInfo>(row.ReleaseJson),
            Reason = (PendingReleaseReason)row.Reason,
            AdditionalInfo = SpacetimeJson.Deserialize<PendingReleaseAdditionalInfo>(row.AdditionalInfoJson)
        };

        protected override int GetRowId(StdbPendingRelease row) => row.Id;

        protected override void InvokeInsertReducer(PendingRelease model) => Conn.Connection.Reducers.InsertPendingRelease(
            model.SeriesId,
            model.Title ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeJson.Serialize(model.ParsedEpisodeInfo),
            SpacetimeJson.Serialize(model.Release),
            (int)model.Reason,
            SpacetimeJson.Serialize(model.AdditionalInfo));

        public override Task MigrateInsert(PendingRelease model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertPendingRelease(
            model.Id,
            model.SeriesId,
            model.Title ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeJson.Serialize(model.ParsedEpisodeInfo),
            SpacetimeJson.Serialize(model.Release),
            (int)model.Reason,
            SpacetimeJson.Serialize(model.AdditionalInfo)));

        protected override void InvokeUpdateReducer(PendingRelease model) => Conn.Connection.Reducers.UpdatePendingRelease(
            model.Id,
            model.SeriesId,
            model.Title ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeJson.Serialize(model.ParsedEpisodeInfo),
            SpacetimeJson.Serialize(model.Release),
            (int)model.Reason,
            SpacetimeJson.Serialize(model.AdditionalInfo));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeletePendingRelease(id);

        public async Task DeleteBySeriesIds(List<int> seriesIds)
        {
            foreach (var row in (await All()).Where(r => seriesIds.Contains(r.SeriesId)).ToList())
            {
                await Delete(row.Id);
            }
        }

        public Task<List<PendingRelease>> AllBySeriesId(int seriesId) =>
            Query(t => t.Iter().Where(r => r.SeriesId == seriesId).Select(ToModel).ToList());

        // No production caller (confirmed by the Phase 3 adversarial review) and the join it did
        // in SQL projected no columns from Series anyway - a plain filter is equivalent.
        public async Task<List<PendingRelease>> WithoutFallback() =>
            (await All()).Where(r => r.Reason != PendingReleaseReason.Fallback).ToList();
    }
}
