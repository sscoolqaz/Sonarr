using System;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbQualityDefinition = SpacetimeDB.Types.QualityDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeQualityDefinitionRepository : SpacetimeBasicRepository<QualityDefinition, StdbQualityDefinition>, IQualityDefinitionRepository
    {
        public SpacetimeQualityDefinitionRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbQualityDefinition> Table => Conn.Connection.Db.QualityDefinition;

        protected override StdbQualityDefinition FindRowById(int id) => Conn.Connection.Db.QualityDefinition.Id.Find(id);

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

            Conn.Connection.Reducers.OnUpdateQualityDefinition += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateQualityDefinition -= Handler);
        }

        // GroupName/Weight/MinSize/MaxSize/PreferredSize are Ignore()'d in the SQL mapping too -
        // not persisted here either, left at their C# defaults same as the SQL path.
        protected override QualityDefinition ToModel(StdbQualityDefinition row) => new QualityDefinition
        {
            Id = row.Id,
            Quality = SpacetimeJson.Deserialize<Quality>(row.QualityJson),
            Title = row.Title
        };

        protected override int GetRowId(StdbQualityDefinition row) => row.Id;

        protected override void InvokeInsertReducer(QualityDefinition model) =>
            Conn.Connection.Reducers.InsertQualityDefinition(SpacetimeJson.Serialize(model.Quality), model.Title ?? string.Empty);

        public override Task MigrateInsert(QualityDefinition model) =>
            InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertQualityDefinition(model.Id, SpacetimeJson.Serialize(model.Quality), model.Title ?? string.Empty));

        protected override void InvokeUpdateReducer(QualityDefinition model) =>
            Conn.Connection.Reducers.UpdateQualityDefinition(model.Id, SpacetimeJson.Serialize(model.Quality), model.Title ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteQualityDefinition(id);
    }
}
