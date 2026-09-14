using System;
using System.Linq;
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

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, int p3, SpacetimeDB.Timestamp p4, int p5, SpacetimeDB.Timestamp p6)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateScheduledTask += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateScheduledTask -= Handler);
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

        public override void MigrateInsert(ScheduledTask model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertScheduledTask(
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

        public ScheduledTask GetDefinition(Type type)
        {
            var typeName = type.FullName;
            return Query(t => t.Iter().Where(r => r.TypeName == typeName).Select(ToModel).Single());
        }

        public void SetLastExecutionTime(int id, DateTime executionTime, DateTime startTime)
        {
            var task = new ScheduledTask { Id = id, LastExecution = executionTime, LastStartTime = startTime };

            SetFields(task, scheduledTask => scheduledTask.LastExecution, scheduledTask => scheduledTask.LastStartTime);
        }
    }
}
