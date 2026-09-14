using System;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbScheduledTask = SpacetimeDB.Types.ScheduledTask;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeScheduledTaskRepository : SpacetimeBasicRepository<ScheduledTask, StdbScheduledTask>, IScheduledTaskRepository
    {
        public SpacetimeScheduledTaskRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbScheduledTask> Table => Conn.Connection.Db.ScheduledTask;

        protected override StdbScheduledTask FindRowById(int id) => Conn.Connection.Db.ScheduledTask.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, int p3, SpacetimeDB.Timestamp p4, int p5, SpacetimeDB.Timestamp p6)
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

            Conn.Connection.Reducers.OnUpdateScheduledTask += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateScheduledTask -= Handler);
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

            Conn.Connection.Reducers.OnDeleteScheduledTask += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteScheduledTask -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, string p1, int p2, SpacetimeDB.Timestamp p3, int p4, SpacetimeDB.Timestamp p5)
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

            Conn.Connection.Reducers.OnInsertScheduledTask += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertScheduledTask -= Handler);
        }

        protected override ScheduledTask ToModel(StdbScheduledTask row) => new ScheduledTask
        {
            Id = row.Id,
            TypeName = row.TypeName,
            Interval = row.Interval,
            LastExecution = SpacetimeDateTime.ToDateTime(row.LastExecution),
            Priority = (Messaging.Commands.CommandPriority)row.Priority,
            LastStartTime = SpacetimeDateTime.ToDateTime(row.LastStartTime)
        };

        protected override int GetRowId(StdbScheduledTask row) => row.Id;

        protected override void InvokeInsertReducer(ScheduledTask model) => Conn.Connection.Reducers.InsertScheduledTask(
            model.TypeName ?? string.Empty,
            model.Interval,
            SpacetimeDateTime.ToTimestamp(model.LastExecution),
            (int)model.Priority,
            SpacetimeDateTime.ToTimestamp(model.LastStartTime));

        public override Task MigrateInsert(ScheduledTask model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertScheduledTask(
            model.Id,
            model.TypeName ?? string.Empty,
            model.Interval,
            SpacetimeDateTime.ToTimestamp(model.LastExecution),
            (int)model.Priority,
            SpacetimeDateTime.ToTimestamp(model.LastStartTime)));

        protected override void InvokeUpdateReducer(ScheduledTask model) => Conn.Connection.Reducers.UpdateScheduledTask(
            model.Id,
            model.TypeName ?? string.Empty,
            model.Interval,
            SpacetimeDateTime.ToTimestamp(model.LastExecution),
            (int)model.Priority,
            SpacetimeDateTime.ToTimestamp(model.LastStartTime));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteScheduledTask(id);

        public Task<ScheduledTask> GetDefinition(Type type)
        {
            var typeName = type.FullName;
            return Query(t => t.Iter().Where(r => r.TypeName == typeName).Select(ToModel).Single());
        }

        public Task SetLastExecutionTime(int id, DateTime executionTime, DateTime startTime)
        {
            var task = new ScheduledTask { Id = id, LastExecution = executionTime, LastStartTime = startTime };

            return SetFields(task, scheduledTask => scheduledTask.LastExecution, scheduledTask => scheduledTask.LastStartTime);
        }
    }
}
