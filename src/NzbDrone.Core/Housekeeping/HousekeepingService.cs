using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Housekeeping
{
    public class HousekeepingService : IExecute<HousekeepingCommand>
    {
        private readonly IEnumerable<IHousekeepingTask> _housekeepers;
        private readonly Logger _logger;

        public HousekeepingService(IEnumerable<IHousekeepingTask> housekeepers, Logger logger)
        {
            _housekeepers = housekeepers;
            _logger = logger;
        }

        private void Clean()
        {
            _logger.Info("Running housecleaning tasks");

            foreach (var housekeeper in _housekeepers)
            {
                try
                {
                    _logger.Debug("Starting {0}", housekeeper.GetType().Name);
                    housekeeper.Clean();
                    _logger.Debug("Completed {0}", housekeeper.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error running housekeeping task: {0}", housekeeper.GetType().Name);
                }
            }
        }

        public void Execute(HousekeepingCommand message)
        {
            Clean();
        }
    }
}
