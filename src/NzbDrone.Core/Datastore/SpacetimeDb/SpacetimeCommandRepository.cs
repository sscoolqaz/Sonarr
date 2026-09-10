using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NzbDrone.Common.Reflection;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
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

        protected override StdbCommand[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.Command.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

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

        public void Trim()
        {
            var date = DateTime.UtcNow.AddDays(-1);

            foreach (var row in All().Where(c => c.EndedAt < date).ToList())
            {
                Delete(row.Id);
            }
        }

        public void OrphanStarted() => Conn.Connection.Reducers.OrphanStartedCommands((int)CommandStatus.Orphaned, (int)CommandStatus.Started, SpacetimeDateTime.ToTimestamp(DateTime.UtcNow));

        public List<CommandModel> Queued() => All().Where(x => x.Status == CommandStatus.Queued).ToList();

        public void Start(CommandModel command) => SetFields(command, c => c.StartedAt, c => c.Status);

        public void End(CommandModel command) => SetFields(command, c => c.EndedAt, c => c.Status, c => c.Duration, c => c.Exception);
    }
}
