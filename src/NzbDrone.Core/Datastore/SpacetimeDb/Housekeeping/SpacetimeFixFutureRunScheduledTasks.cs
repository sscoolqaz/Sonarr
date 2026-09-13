using System;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.Jobs;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Real FixFutureRunScheduledTasks runs a raw SQL UPDATE against IMainDatabase directly -
    // a no-op once SpacetimeDB is the write path, same reasoning as
    // SpacetimeCleanupOrphanedEpisodes for why this is additional, not a replacement. The real
    // task logs a debug-mode notice but still runs the update afterward - only the log line is
    // debug-gated there, not the update itself - so this mirrors that exactly rather than
    // skipping the update.
    public class SpacetimeFixFutureRunScheduledTasks : IHousekeepingTask
    {
        private readonly IScheduledTaskRepository _scheduledTaskRepository;
        private readonly Logger _logger;

        public SpacetimeFixFutureRunScheduledTasks(IScheduledTaskRepository scheduledTaskRepository, Logger logger)
        {
            _scheduledTaskRepository = scheduledTaskRepository;
            _logger = logger;
        }

        public void Clean()
        {
            if (BuildInfo.IsDebug)
            {
                _logger.Debug("Not running scheduled task last execution cleanup during debug");
            }

            var now = DateTime.UtcNow;
            var future = _scheduledTaskRepository.All().Where(t => t.LastExecution > now).ToList();

            foreach (var task in future)
            {
                task.LastExecution = now;
                _scheduledTaskRepository.SetFields(task, t => t.LastExecution);
            }
        }
    }
}
