using System;
using System.Linq;
using NzbDrone.Core.Jobs;
using NzbDrone.Core.Messaging.Events;
using StdbScheduledTask = SpacetimeDB.Types.ScheduledTask;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeScheduledTaskRepository : SpacetimeBasicRepository<ScheduledTask, StdbScheduledTask>, IScheduledTaskRepository
    {
        public SpacetimeScheduledTaskRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbScheduledTask[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.ScheduledTask.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

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

        public override void MigrateInsert(ScheduledTask model) => Conn.Connection.Reducers.MigrateInsertScheduledTask(
            model.Id,
            model.TypeName ?? string.Empty,
            model.Interval,
            SpacetimeDateTime.ToTimestamp(model.LastExecution),
            (int)model.Priority,
            SpacetimeDateTime.ToTimestamp(model.LastStartTime));

        protected override void InvokeUpdateReducer(ScheduledTask model) => Conn.Connection.Reducers.UpdateScheduledTask(
            model.Id,
            model.TypeName ?? string.Empty,
            model.Interval,
            SpacetimeDateTime.ToTimestamp(model.LastExecution),
            (int)model.Priority,
            SpacetimeDateTime.ToTimestamp(model.LastStartTime));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteScheduledTask(id);

        public ScheduledTask GetDefinition(Type type) =>
            RemoteQuery($"WHERE TypeName = '{EscapeSqlString(type.FullName)}'").Select(ToModel).Single();

        public void SetLastExecutionTime(int id, DateTime executionTime, DateTime startTime)
        {
            var task = new ScheduledTask { Id = id, LastExecution = executionTime, LastStartTime = startTime };

            SetFields(task, scheduledTask => scheduledTask.LastExecution, scheduledTask => scheduledTask.LastStartTime);
        }
    }
}
