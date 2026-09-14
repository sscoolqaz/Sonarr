using System;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Jobs
{
    public interface IScheduledTaskRepository : IBasicRepository<ScheduledTask>
    {
        Task<ScheduledTask> GetDefinition(Type type);
        Task SetLastExecutionTime(int id, DateTime executionTime, DateTime startTime);
    }
}
