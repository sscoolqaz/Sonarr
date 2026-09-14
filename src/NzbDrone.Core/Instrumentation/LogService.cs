using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Instrumentation.Commands;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Instrumentation
{
    public interface ILogService
    {
        Task<PagingSpec<Log>> Paged(PagingSpec<Log> pagingSpec);
    }

    public class LogService : ILogService, IExecute<ClearLogCommand>
    {
        private readonly ILogRepository _logRepository;

        public LogService(ILogRepository logRepository)
        {
            _logRepository = logRepository;
        }

        public Task<PagingSpec<Log>> Paged(PagingSpec<Log> pagingSpec)
        {
            return _logRepository.GetPaged(pagingSpec);
        }

        // NOTE: IExecute<TCommand> is a shared command-eventing interface (31+ implementers
        // app-wide); its `void Execute(TCommand message)` signature is out of scope to change.
        // Commands run off the request thread via the same EventAggregator/Task.Factory.StartNew
        // path as IHandle<TEvent>, with no SynchronizationContext, so bridging here via
        // GetAwaiter().GetResult() cannot deadlock.
        public void Execute(ClearLogCommand message)
        {
            _logRepository.Purge(vacuum: true).GetAwaiter().GetResult();
        }
    }
}
