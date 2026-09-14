using System;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbConfig = SpacetimeDB.Types.Config;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeConfigRepository : SpacetimeBasicRepository<Config, StdbConfig>, IConfigRepository
    {
        public SpacetimeConfigRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbConfig> Table => Conn.Connection.Db.Config;

        protected override StdbConfig FindRowById(int id) => Conn.Connection.Db.Config.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3)
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

            Conn.Connection.Reducers.OnUpdateConfig += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateConfig -= Handler);
        }

        protected override Config ToModel(StdbConfig row) => new Config { Id = row.Id, Key = row.Key, Value = row.Value };

        protected override int GetRowId(StdbConfig row) => row.Id;

        protected override void InvokeInsertReducer(Config model) => Conn.Connection.Reducers.InsertConfig(model.Key ?? string.Empty, model.Value ?? string.Empty);

        public override Task MigrateInsert(Config model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertConfig(model.Id, model.Key ?? string.Empty, model.Value ?? string.Empty));

        protected override void InvokeUpdateReducer(Config model) => Conn.Connection.Reducers.UpdateConfig(model.Id, model.Key ?? string.Empty, model.Value ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteConfig(id);

        public Task<Config> Get(string key) =>
            Query(t => t.Iter().Where(r => r.Key == key).Select(ToModel).SingleOrDefault());

        public async Task<Config> Upsert(string key, string value)
        {
            var dbValue = await Get(key);

            if (dbValue == null)
            {
                return await Insert(new Config { Key = key, Value = value });
            }

            dbValue.Value = value;

            return await Update(dbValue);
        }
    }
}
