using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbCustomFormat = SpacetimeDB.Types.CustomFormat;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeCustomFormatRepository : SpacetimeBasicRepository<CustomFormat, StdbCustomFormat>, ICustomFormatRepository
    {
        // Reuses the same converter the SQL repository's Dapper type handler wraps - Specifications
        // is genuinely polymorphic (one concrete ICustomFormatSpecification per format-condition
        // type), and this converter already does the safe, assembly-restricted type resolution
        // (Type.GetType("NzbDrone.Core.CustomFormats.{name}, Sonarr.Core")) rather than a blanket
        // TypeNameHandling.Auto.
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Converters = { new CustomFormatSpecificationListConverter() }
        };

        public SpacetimeCustomFormatRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbCustomFormat> Table => Conn.Connection.Db.CustomFormat;

        protected override StdbCustomFormat FindRowById(int id) => Conn.Connection.Db.CustomFormat.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, bool p3, string p4)
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

            Conn.Connection.Reducers.OnUpdateCustomFormat += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateCustomFormat -= Handler);
        }

        protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id)
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

            Conn.Connection.Reducers.OnDeleteCustomFormat += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteCustomFormat -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, string p1, bool p2, string p3)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted();
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnInsertCustomFormat += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertCustomFormat -= Handler);
        }

        protected override CustomFormat ToModel(StdbCustomFormat row) => MapRow(row);

        /// <summary>
        /// Pure row-to-model mapping with no dependency on Conn or any other repository/service -
        /// exposed so a sibling repository's own ToModel (e.g.
        /// SpacetimeQualityProfileRepository, populating its FormatItems) can read
        /// SpacetimeDB.Types.CustomFormat rows straight off Conn.Connection.Db.CustomFormat and
        /// map them directly, instead of re-entering this repository's own public async API
        /// (ICustomFormatRepository/ICustomFormatService) from inside another repository's
        /// already-actor-thread ToModel call - see SpacetimeQualityProfileRepository.MapRow's own
        /// remarks for the full cross-repository-read reasoning.
        /// </summary>
        internal static CustomFormat MapRow(StdbCustomFormat row) => new CustomFormat
        {
            Id = row.Id,
            Name = row.Name,
            IncludeCustomFormatWhenRenaming = row.IncludeCustomFormatWhenRenaming,
            Specifications = string.IsNullOrEmpty(row.SpecificationsJson)
                ? new List<ICustomFormatSpecification>()
                : JsonSerializer.Deserialize<List<ICustomFormatSpecification>>(row.SpecificationsJson, Options)
        };

        protected override int GetRowId(StdbCustomFormat row) => row.Id;

        protected override void InvokeInsertReducer(CustomFormat model) => Conn.Connection.Reducers.InsertCustomFormat(
            model.Name ?? string.Empty, model.IncludeCustomFormatWhenRenaming, JsonSerializer.Serialize(model.Specifications, Options));

        public override Task MigrateInsert(CustomFormat model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertCustomFormat(
            model.Id, model.Name ?? string.Empty, model.IncludeCustomFormatWhenRenaming, JsonSerializer.Serialize(model.Specifications, Options)));

        protected override void InvokeUpdateReducer(CustomFormat model) => Conn.Connection.Reducers.UpdateCustomFormat(
            model.Id, model.Name ?? string.Empty, model.IncludeCustomFormatWhenRenaming, JsonSerializer.Serialize(model.Specifications, Options));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteCustomFormat(id);
    }
}
