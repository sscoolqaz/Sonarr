using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Composition;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public interface IManageCommandQueue
    {
        Task<List<CommandModel>> PushMany<TCommand>(List<TCommand> commands)
            where TCommand : Command;
        Task<CommandModel> Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
            where TCommand : Command;
        Task<CommandModel> Push(string commandName, DateTime? lastExecutionTime, DateTime? lastStartTime, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified);
        IEnumerable<CommandModel> Queue(CancellationToken cancellationToken);
        List<CommandModel> All();
        Task<CommandModel> Get(int id);
        List<CommandModel> GetStarted();
        void SetMessage(CommandModel command, string message);
        void SetResult(CommandModel command, CommandResult result);
        Task Start(CommandModel command);
        Task Complete(CommandModel command, string message);
        Task Fail(CommandModel command, string message, Exception e);
        Task Requeue();
        void Cancel(int id);
        Task CleanCommands();
    }

    public class CommandQueueManager : IManageCommandQueue, IHandle<ApplicationStartedEvent>
    {
        private readonly ICommandRepository _repo;
        private readonly KnownTypes _knownTypes;
        private readonly Logger _logger;

        private readonly CommandQueue _commandQueue;

        // NOTE: was a plain `lock (_commandQueue)`; converted to SemaphoreSlim since the critical
        // section now needs to `await` repository calls, which C#'s `lock` forbids.
        private readonly SemaphoreSlim _syncRoot = new SemaphoreSlim(1, 1);

        public CommandQueueManager(ICommandRepository repo,
                                   IServiceFactory serviceFactory,
                                   KnownTypes knownTypes,
                                   Logger logger)
        {
            _repo = repo;
            _knownTypes = knownTypes;
            _logger = logger;

            _commandQueue = new CommandQueue();
        }

        public async Task<List<CommandModel>> PushMany<TCommand>(List<TCommand> commands)
            where TCommand : Command
        {
            _logger.Trace("Publishing {0} commands", commands.Count);

            await _syncRoot.WaitAsync();

            try
            {
                var commandModels = new List<CommandModel>();
                var existingCommands = _commandQueue.QueuedOrStarted();

                foreach (var command in commands)
                {
                    var existing = existingCommands.FirstOrDefault(c => c.Name == command.Name && CommandEqualityComparer.Instance.Equals(c.Body, command));

                    if (existing != null)
                    {
                        continue;
                    }

                    var commandModel = new CommandModel
                    {
                        Name = command.Name,
                        Body = command,
                        QueuedAt = DateTime.UtcNow,
                        Trigger = CommandTrigger.Unspecified,
                        Priority = CommandPriority.Normal,
                        Status = CommandStatus.Queued
                    };

                    commandModels.Add(commandModel);
                }

                await _repo.InsertMany(commandModels);

                foreach (var commandModel in commandModels)
                {
                    _commandQueue.Add(commandModel);
                }

                return commandModels;
            }
            finally
            {
                _syncRoot.Release();
            }
        }

        public async Task<CommandModel> Push<TCommand>(TCommand command, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
            where TCommand : Command
        {
            Ensure.That(command, () => command).IsNotNull();

            _logger.Trace("Publishing {0}", command.Name);
            _logger.Trace("Checking if command is queued or started: {0}", command.Name);

            command.Trigger = trigger;

            await _syncRoot.WaitAsync();

            try
            {
                var existingCommands = QueuedOrStarted(command.Name);
                var existing = existingCommands.FirstOrDefault(c => CommandEqualityComparer.Instance.Equals(c.Body, command));

                if (existing != null)
                {
                    _logger.Trace("Command is already in progress: {0}", command.Name);

                    return existing;
                }

                var commandModel = new CommandModel
                {
                    Name = command.Name,
                    Body = command,
                    QueuedAt = DateTime.UtcNow,
                    Trigger = trigger,
                    Priority = priority,
                    Status = CommandStatus.Queued
                };

                _logger.Trace("Inserting new command: {0}", commandModel.Name);

                await _repo.Insert(commandModel);
                _commandQueue.Add(commandModel);

                return commandModel;
            }
            finally
            {
                _syncRoot.Release();
            }
        }

        public async Task<CommandModel> Push(string commandName, DateTime? lastExecutionTime, DateTime? lastStartTime, CommandPriority priority = CommandPriority.Normal, CommandTrigger trigger = CommandTrigger.Unspecified)
        {
            var command = GetCommand(commandName);
            command.LastExecutionTime = lastExecutionTime;
            command.LastStartTime = lastStartTime;

            return await Push(command, priority, trigger);
        }

        public IEnumerable<CommandModel> Queue(CancellationToken cancellationToken)
        {
            return _commandQueue.GetConsumingEnumerable(cancellationToken);
        }

        public List<CommandModel> All()
        {
            _logger.Trace("Getting all commands");
            return _commandQueue.All();
        }

        public async Task<CommandModel> Get(int id)
        {
            var command = _commandQueue.Find(id);

            if (command == null)
            {
                command = await _repo.Get(id);
            }

            return command;
        }

        public List<CommandModel> GetStarted()
        {
            _logger.Trace("Getting started commands");
            return _commandQueue.All().Where(c => c.Status == CommandStatus.Started).ToList();
        }

        public void SetMessage(CommandModel command, string message)
        {
            command.Message = message;
        }

        public void SetResult(CommandModel command, CommandResult result)
        {
            command.Result = result;
        }

        public async Task Start(CommandModel command)
        {
            // Marks the command as started in the DB, the queue takes care of marking it as started on it's own
            _logger.Trace("Marking command as started: {0}", command.Name);
            await _repo.Start(command);
        }

        public async Task Complete(CommandModel command, string message)
        {
            // If the result hasn't been set yet then set it to successful
            if (command.Result == CommandResult.Unknown)
            {
                command.Result = CommandResult.Successful;
            }

            await Update(command, CommandStatus.Completed, message);

            _commandQueue.PulseAllConsumers();
        }

        public async Task Fail(CommandModel command, string message, Exception e)
        {
            command.Exception = e.ToString();

            await Update(command, CommandStatus.Failed, message);

            _commandQueue.PulseAllConsumers();
        }

        public async Task Requeue()
        {
            foreach (var command in await _repo.Queued())
            {
                _commandQueue.Add(command);
            }
        }

        public void Cancel(int id)
        {
            if (!_commandQueue.RemoveIfQueued(id))
            {
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Unable to cancel task");
            }
        }

        public async Task CleanCommands()
        {
            _logger.Trace("Cleaning up old commands");

            var commands = _commandQueue.All()
                                        .Where(c => c.EndedAt < DateTime.UtcNow.AddMinutes(-5))
                                        .ToList();

            _commandQueue.RemoveMany(commands);

            await _repo.Trim();
        }

        private Command GetCommand(string commandName)
        {
            commandName = commandName.Split('.').Last();
            var commands = _knownTypes.GetImplementations(typeof(Command));
            var commandType = commands.Single(c => c.Name.Equals(commandName, StringComparison.InvariantCultureIgnoreCase));

            return Json.Deserialize("{}", commandType) as Command;
        }

        private async Task Update(CommandModel command, CommandStatus status, string message)
        {
            SetMessage(command, message);

            command.EndedAt = DateTime.UtcNow;
            command.Duration = command.EndedAt.Value.Subtract(command.StartedAt.Value);
            command.Status = status;

            _logger.Trace("Updating command status");
            await _repo.End(command);
        }

        private List<CommandModel> QueuedOrStarted(string name)
        {
            return _commandQueue.QueuedOrStarted()
                                .Where(q => q.Name == name)
                                .ToList();
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide); its
        // `void Handle(TEvent message)` signature is out of scope to convert (see architectural
        // note in ProviderFactory.cs). Blocking here via GetAwaiter().GetResult() is the
        // documented boundary.
        public void Handle(ApplicationStartedEvent message)
        {
            _logger.Trace("Orphaning incomplete commands");
            _repo.OrphanStarted().GetAwaiter().GetResult();
            Requeue().GetAwaiter().GetResult();
        }
    }
}
