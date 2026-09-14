using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        private async Task Clean()
        {
            _logger.Info("Running housecleaning tasks");

            foreach (var housekeeper in _housekeepers)
            {
                try
                {
                    _logger.Debug("Starting {0}", housekeeper.GetType().Name);
                    await housekeeper.Clean();
                    _logger.Debug("Completed {0}", housekeeper.GetType().Name);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error running housekeeping task: {0}", housekeeper.GetType().Name);
                }
            }
        }

        // NOTE: IExecute<TCommand> is a shared command-eventing interface (31+ implementers
        // app-wide); its `void Execute(TCommand message)` signature is out of scope to change.
        // Commands run off the request thread via the same EventAggregator/Task.Factory.StartNew
        // path as IHandle<TEvent>, with no SynchronizationContext, so bridging here via
        // GetAwaiter().GetResult() cannot deadlock.
        public void Execute(HousekeepingCommand message)
        {
            Clean().GetAwaiter().GetResult();
        }
    }
}
