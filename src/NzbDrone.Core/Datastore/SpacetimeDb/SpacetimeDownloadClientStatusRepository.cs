using System;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbDownloadClientStatus = SpacetimeDB.Types.DownloadClientStatus;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDownloadClientStatusRepository : SpacetimeProviderStatusRepository<DownloadClientStatus, StdbDownloadClientStatus>, IDownloadClientStatusRepository
    {
        public SpacetimeDownloadClientStatusRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbDownloadClientStatus> Table => Conn.Connection.Db.DownloadClientStatus;

        protected override StdbDownloadClientStatus FindRowById(int id) => Conn.Connection.Db.DownloadClientStatus.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, SpacetimeDB.Timestamp? p3, SpacetimeDB.Timestamp? p4, int p5, SpacetimeDB.Timestamp? p6)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateDownloadClientStatus += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateDownloadClientStatus -= Handler);
        }

        protected override DownloadClientStatus ToModel(StdbDownloadClientStatus row) => new DownloadClientStatus
        {
            Id = row.Id,
            ProviderId = row.ProviderId,
            InitialFailure = SpacetimeDateTime.ToDateTime(row.InitialFailure),
            MostRecentFailure = SpacetimeDateTime.ToDateTime(row.MostRecentFailure),
            EscalationLevel = row.EscalationLevel,
            DisabledTill = SpacetimeDateTime.ToDateTime(row.DisabledTill)
        };

        protected override int GetRowId(StdbDownloadClientStatus row) => row.Id;

        protected override void InvokeInsertReducer(DownloadClientStatus model) => Conn.Connection.Reducers.InsertDownloadClientStatus(
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeUpdateReducer(DownloadClientStatus model) => Conn.Connection.Reducers.UpdateDownloadClientStatus(
            model.Id,
            model.ProviderId,
            SpacetimeDateTime.ToTimestamp(model.InitialFailure),
            SpacetimeDateTime.ToTimestamp(model.MostRecentFailure),
            model.EscalationLevel,
            SpacetimeDateTime.ToTimestamp(model.DisabledTill));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteDownloadClientStatus(id);
    }
}
