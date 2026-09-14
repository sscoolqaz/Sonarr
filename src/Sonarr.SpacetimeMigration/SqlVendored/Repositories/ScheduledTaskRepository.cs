using System;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Jobs
{
    public class ScheduledTaskRepository : BasicRepository<ScheduledTask>, IScheduledTaskRepository
    {
        public ScheduledTaskRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<ScheduledTask> GetDefinition(Type type)
        {
            return Task.FromResult(Query(c => c.TypeName == type.FullName).Single());
        }

        public Task SetLastExecutionTime(int id, DateTime executionTime, DateTime startTime)
        {
            var task = new ScheduledTask
                {
                    Id = id,
                    LastExecution = executionTime,
                    LastStartTime = startTime
                };

            SetFields(task, scheduledTask => scheduledTask.LastExecution, scheduledTask => scheduledTask.LastStartTime).GetAwaiter().GetResult();

            return Task.CompletedTask;
        }
    }
}
