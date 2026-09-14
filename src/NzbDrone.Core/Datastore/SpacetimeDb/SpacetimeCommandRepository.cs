using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NzbDrone.Common.Reflection;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbCommand = SpacetimeDB.Types.CommandRow;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeCommandRepository : SpacetimeBasicRepository<CommandModel, StdbCommand>, ICommandRepository
    {
        // Matches NzbDrone.Core.Datastore.Converters.EmbeddedDocumentConverter<T>'s settings
        // exactly (camelCase property names, case-insensitive reads, custom TimeSpan/UTC
        // converters) - the "name" discriminator lookup below depends on the writer actually
        // emitting a lowercase "name" property, which only System.Text.Json's default
        // (PascalCase) options would not do.
        private static readonly JsonSerializerOptions SerializerSettings = new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, true), new STJTimeSpanConverter(), new STJUtcConverter() }
        };

        public SpacetimeCommandRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbCommand> Table => Conn.Connection.Db.Command;

        protected override StdbCommand FindRowById(int id) => Conn.Connection.Db.Command.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, int p4, int p5, int p6, SpacetimeDB.Timestamp p7, SpacetimeDB.Timestamp? p8, SpacetimeDB.Timestamp? p9, long? p10, string p11, int p12)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnUpdateCommand += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateCommand -= Handler);
        }

        // Body is polymorphic (one concrete Command subclass per command type). Mirrors
        // NzbDrone.Core.Datastore.Converters.CommandConverter's approach exactly: a "name"
        // discriminator resolved to a concrete type via NzbDrone.Common.Reflection.FindTypeByName
        // restricted to the Command assembly - not an arbitrary type lookup.
        private static Command DeserializeBody(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            string contract;
            using (var doc = JsonDocument.Parse(json))
            {
                contract = doc.RootElement.GetProperty("name").GetString();
            }

            var implementationType = typeof(Command).Assembly.FindTypeByName(contract + "Command");

            return implementationType == null
                ? null
                : (Command)JsonSerializer.Deserialize(json, implementationType, SerializerSettings);
        }

        private static string SerializeBody(Command body) => body == null ? null : JsonSerializer.Serialize((object)body, SerializerSettings);

        protected override CommandModel ToModel(StdbCommand row) => new CommandModel
        {
            Id = row.Id,
            Name = row.Name,
            Body = DeserializeBody(row.BodyJson),
            Priority = (CommandPriority)row.Priority,
            Status = (CommandStatus)row.Status,
            Result = (CommandResult)row.Result,
            QueuedAt = SpacetimeDateTime.ToDateTime(row.QueuedAt),
            StartedAt = SpacetimeDateTime.ToDateTime(row.StartedAt),
            EndedAt = SpacetimeDateTime.ToDateTime(row.EndedAt),
            Duration = row.DurationTicks.HasValue ? TimeSpan.FromTicks(row.DurationTicks.Value) : (TimeSpan?)null,
            Exception = row.Exception,
            Trigger = (CommandTrigger)row.Trigger
        };

        protected override int GetRowId(StdbCommand row) => row.Id;

        protected override void InvokeInsertReducer(CommandModel model) => Conn.Connection.Reducers.InsertCommand(
            model.Name ?? string.Empty,
            SerializeBody(model.Body) ?? string.Empty,
            (int)model.Priority,
            (int)model.Status,
            (int)model.Result,
            SpacetimeDateTime.ToTimestamp(model.QueuedAt),
            SpacetimeDateTime.ToTimestamp(model.StartedAt),
            SpacetimeDateTime.ToTimestamp(model.EndedAt),
            model.Duration?.Ticks,
            model.Exception ?? string.Empty,
            (int)model.Trigger);

        protected override void InvokeUpdateReducer(CommandModel model) => Conn.Connection.Reducers.UpdateCommand(
            model.Id,
            model.Name ?? string.Empty,
            SerializeBody(model.Body) ?? string.Empty,
            (int)model.Priority,
            (int)model.Status,
            (int)model.Result,
            SpacetimeDateTime.ToTimestamp(model.QueuedAt),
            SpacetimeDateTime.ToTimestamp(model.StartedAt),
            SpacetimeDateTime.ToTimestamp(model.EndedAt),
            model.Duration?.Ticks,
            model.Exception ?? string.Empty,
            (int)model.Trigger);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteCommand(id);

        public async Task Trim()
        {
            var date = DateTime.UtcNow.AddDays(-1);

            foreach (var row in (await All()).Where(c => c.EndedAt < date).ToList())
            {
                await Delete(row.Id);
            }
        }

        public Task OrphanStarted() =>
            InvokeAndWaitForReducerCommitted(
                () => Conn.Connection.Reducers.OrphanStartedCommands((int)CommandStatus.Orphaned, (int)CommandStatus.Started, SpacetimeDateTime.ToTimestamp(DateTime.UtcNow)),
                (onCommitted, onFailed) =>
                {
                    void Handler(ReducerEventContext ctx, int orphanedStatus, int startedStatus, SpacetimeDB.Timestamp endedAt)
                    {
                        if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                            ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId)
                        {
                            if (ctx.Event.Status is Status.Committed)
                            {
                                onCommitted();
                            }
                            else if (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy)
                            {
                                onFailed(new InvalidOperationException($"OrphanStartedCommands reducer failed with status {ctx.Event.Status}"));
                            }
                        }
                    }

                    Conn.Connection.Reducers.OnOrphanStartedCommands += Handler;
                    return new Unsubscriber(() => Conn.Connection.Reducers.OnOrphanStartedCommands -= Handler);
                });

        public async Task<List<CommandModel>> Queued() => (await All()).Where(x => x.Status == CommandStatus.Queued).ToList();

        public Task Start(CommandModel command) => SetFields(command, c => c.StartedAt, c => c.Status);

        public Task End(CommandModel command) => SetFields(command, c => c.EndedAt, c => c.Status, c => c.Duration, c => c.Exception);
    }
}
