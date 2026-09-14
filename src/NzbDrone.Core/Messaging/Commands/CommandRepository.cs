using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface ICommandRepository : IBasicRepository<CommandModel>
    {
        void Trim();
        void OrphanStarted();
        List<CommandModel> Queued();
        void Start(CommandModel command);
        void End(CommandModel command);
    }
}
