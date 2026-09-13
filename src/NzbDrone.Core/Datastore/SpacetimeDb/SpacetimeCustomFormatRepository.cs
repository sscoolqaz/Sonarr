using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Messaging.Events;
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

        protected override StdbCustomFormat[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.CustomFormat.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override CustomFormat ToModel(StdbCustomFormat row) => new CustomFormat
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

        public override void MigrateInsert(CustomFormat model) => Conn.Connection.Reducers.MigrateInsertCustomFormat(
            model.Id, model.Name ?? string.Empty, model.IncludeCustomFormatWhenRenaming, JsonSerializer.Serialize(model.Specifications, Options));

        protected override void InvokeUpdateReducer(CustomFormat model) => Conn.Connection.Reducers.UpdateCustomFormat(
            model.Id, model.Name ?? string.Empty, model.IncludeCustomFormatWhenRenaming, JsonSerializer.Serialize(model.Specifications, Options));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteCustomFormat(id);
    }
}
