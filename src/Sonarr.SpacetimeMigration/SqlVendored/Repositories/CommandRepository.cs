using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Messaging.Commands
{
    public class CommandRepository : BasicRepository<CommandModel>, ICommandRepository
    {
        public CommandRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task Trim()
        {
            var date = DateTime.UtcNow.AddDays(-1);

            Delete(c => c.EndedAt < date);

            return Task.CompletedTask;
        }

        public Task OrphanStarted()
        {
            var sql = @"UPDATE ""Commands"" SET ""Status"" = @Orphaned, ""EndedAt"" = @Ended WHERE ""Status"" = @Started";
            var args = new
                {
                    Orphaned = (int)CommandStatus.Orphaned,
                    Started = (int)CommandStatus.Started,
                    Ended = DateTime.UtcNow
                };

            using (var conn = _database.OpenConnection())
            {
                conn.Execute(sql, args);
            }

            return Task.CompletedTask;
        }

        public Task<List<CommandModel>> Queued()
        {
            return Task.FromResult(Query(x => x.Status == CommandStatus.Queued));
        }

        public Task Start(CommandModel command)
        {
            SetFields(command, c => c.StartedAt, c => c.Status).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task End(CommandModel command)
        {
            SetFields(command, c => c.EndedAt, c => c.Status, c => c.Duration, c => c.Exception).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
    }
}
