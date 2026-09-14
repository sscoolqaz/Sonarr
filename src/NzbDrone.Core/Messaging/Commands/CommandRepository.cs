using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandRepository : IBasicRepository<CommandModel>
    {
        Task Trim();
        Task OrphanStarted();
        Task<List<CommandModel>> Queued();
        Task Start(CommandModel command);
        Task End(CommandModel command);
    }
}
