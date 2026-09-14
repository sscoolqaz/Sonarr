using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupCommandQueue : IHousekeepingTask
    {
        private readonly IManageCommandQueue _commandQueueManager;

        public CleanupCommandQueue(IManageCommandQueue commandQueueManager)
        {
            _commandQueueManager = commandQueueManager;
        }

        public Task Clean()
        {
            return _commandQueueManager.CleanCommands();
        }
    }
}
