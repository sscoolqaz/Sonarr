using System;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbIndexerDefinition = SpacetimeDB.Types.IndexerDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeIndexerRepository : SpacetimeProviderRepository<IndexerDefinition, StdbIndexerDefinition>, IIndexerRepository
    {
        public SpacetimeIndexerRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbIndexerDefinition> Table => Conn.Connection.Db.IndexerDefinition;

        protected override StdbIndexerDefinition FindRowById(int id) => Conn.Connection.Db.IndexerDefinition.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4, string p5, bool p6, string p7, string p8)
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

            Conn.Connection.Reducers.OnUpdateIndexerDefinition += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateIndexerDefinition -= Handler);
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

            Conn.Connection.Reducers.OnDeleteIndexerDefinition += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteIndexerDefinition -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, string p1, string p2, string p3, string p4, bool p5, string p6, string p7)
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

            Conn.Connection.Reducers.OnInsertIndexerDefinition += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertIndexerDefinition -= Handler);
        }

        protected override IndexerDefinition ToModel(StdbIndexerDefinition row) => new IndexerDefinition
        {
            Id = row.Id,
            Name = row.Name,
            Implementation = row.Implementation,
            ConfigContract = row.ConfigContract,
            Settings = DeserializeSettings(row.ConfigContract, row.SettingsJson),
            Enable = row.Enable,
            Tags = DeserializeTags(row.TagsJson),
            Message = DeserializeMessage(row.MessageJson)
        };

        protected override int GetRowId(StdbIndexerDefinition row) => row.Id;

        protected override void InvokeInsertReducer(IndexerDefinition model) => Conn.Connection.Reducers.InsertIndexerDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        public override Task MigrateInsert(IndexerDefinition model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertIndexerDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message)));

        protected override void InvokeUpdateReducer(IndexerDefinition model) => Conn.Connection.Reducers.UpdateIndexerDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteIndexerDefinition(id);

        public Task<IndexerDefinition> FindByName(string name) =>
            Query(t => t.Iter().Where(r => r.Name == name).Select(ToModel).SingleOrDefault());
    }
}
