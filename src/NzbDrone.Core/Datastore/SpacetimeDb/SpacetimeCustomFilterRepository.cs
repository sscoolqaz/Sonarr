using System;
using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbCustomFilter = SpacetimeDB.Types.CustomFilter;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeCustomFilterRepository : SpacetimeBasicRepository<CustomFilter, StdbCustomFilter>, ICustomFilterRepository
    {
        public SpacetimeCustomFilterRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbCustomFilter> Table => Conn.Connection.Db.CustomFilter;

        protected override StdbCustomFilter FindRowById(int id) => Conn.Connection.Db.CustomFilter.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateCustomFilter += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateCustomFilter -= Handler);
        }

        protected override CustomFilter ToModel(StdbCustomFilter row) =>
            new CustomFilter { Id = row.Id, Type = row.Type, Label = row.Label, Filters = row.Filters };

        protected override int GetRowId(StdbCustomFilter row) => row.Id;

        protected override void InvokeInsertReducer(CustomFilter model) =>
            Conn.Connection.Reducers.InsertCustomFilter(model.Type ?? string.Empty, model.Label ?? string.Empty, model.Filters ?? string.Empty);

        public override void MigrateInsert(CustomFilter model) =>
            InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertCustomFilter(model.Id, model.Type ?? string.Empty, model.Label ?? string.Empty, model.Filters ?? string.Empty));

        protected override void InvokeUpdateReducer(CustomFilter model) =>
            Conn.Connection.Reducers.UpdateCustomFilter(model.Id, model.Type ?? string.Empty, model.Label ?? string.Empty, model.Filters ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteCustomFilter(id);
    }
}
