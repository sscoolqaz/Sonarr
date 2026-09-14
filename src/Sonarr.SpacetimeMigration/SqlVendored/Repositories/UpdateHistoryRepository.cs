using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Update.History
{
    public class UpdateHistoryRepository : BasicRepository<UpdateHistory>, IUpdateHistoryRepository
    {
        public UpdateHistoryRepository(ILogDatabase logDatabase, IEventAggregator eventAggregator)
            : base(logDatabase, eventAggregator)
        {
        }

        public Task<UpdateHistory> LastInstalled()
        {
            var history = Query(v => v.EventType == UpdateHistoryEventType.Installed)
                               .OrderByDescending(v => v.Date)
                               .Take(1)
                               .FirstOrDefault();

            return Task.FromResult(history);
        }

        public Task<UpdateHistory> PreviouslyInstalled()
        {
            var history = Query(v => v.EventType == UpdateHistoryEventType.Installed)
                               .OrderByDescending(v => v.Date)
                               .Skip(1)
                               .Take(1)
                               .FirstOrDefault();

            return Task.FromResult(history);
        }

        public Task<List<UpdateHistory>> InstalledSince(DateTime dateTime)
        {
            var history = Query(v => v.EventType == UpdateHistoryEventType.Installed && v.Date >= dateTime)
                               .OrderBy(v => v.Date)
                               .ToList();

            return Task.FromResult(history);
        }
    }
}
