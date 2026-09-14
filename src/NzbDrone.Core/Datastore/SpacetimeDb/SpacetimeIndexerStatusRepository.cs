using System;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbIndexerStatus = SpacetimeDB.Types.IndexerStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeIndexerStatusRepository : SpacetimeProviderStatusRepository<IndexerStatus, StdbIndexerStatus>, IIndexerStatusRepository
    {
        public SpacetimeIndexerStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbIndexerStatus> Table => Conn.Connection.Db.IndexerStatus;

        protected override StdbIndexerStatus FindRowById(int id) => Conn.Connection.Db.IndexerStatus.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, SpacetimeDB.Timestamp? p3, SpacetimeDB.Timestamp? p4, int p5, SpacetimeDB.Timestamp? p6, string p7)
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

            Conn.Connection.Reducers.OnUpdateIndexerStatus += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateIndexerStatus -= Handler);
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

            Conn.Connection.Reducers.OnDeleteIndexerStatus += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteIndexerStatus -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int p1, SpacetimeDB.Timestamp? p2, SpacetimeDB.Timestamp? p3, int p4, SpacetimeDB.Timestamp? p5, string p6)
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

            Conn.Connection.Reducers.OnInsertIndexerStatus += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertIndexerStatus -= Handler);
        }

        protected override IndexerStatus ToModel(StdbIndexerStatus row) => new IndexerStatus
        {
            Id = row.Id,
            ProviderId = row.ProviderId,
            InitialFailure = SpacetimeDateTime.ToDateTime(row.InitialFailure),
            MostRecentFailure = SpacetimeDateTime.ToDateTime(row.MostRecentFailure),
            EscalationLevel = row.EscalationLevel,
            DisabledTill = SpacetimeDateTime.ToDateTime(row.DisabledTill),
            LastRssSyncReleaseInfo = SpacetimeJson.Deserialize<ReleaseInfo>(row.LastRssSyncReleaseInfoJson)
        };

        protected override int GetRowId(StdbIndexerStatus row) => row.Id;

        protected override void InvokeInsertReducer(IndexerStatus model) => Conn.Connection.Reducers.InsertIndexerStatus(
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill),
            SpacetimeJson.Serialize(model.LastRssSyncReleaseInfo));

        protected override void InvokeUpdateReducer(IndexerStatus model) => Conn.Connection.Reducers.UpdateIndexerStatus(
            model.Id,
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill),
            SpacetimeJson.Serialize(model.LastRssSyncReleaseInfo));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteIndexerStatus(id);
    }
}
