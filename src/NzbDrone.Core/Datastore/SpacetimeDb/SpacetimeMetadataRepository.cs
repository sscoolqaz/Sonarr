using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Messaging.Events;
using StdbMetadataDefinition = SpacetimeDB.Types.MetadataDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeMetadataRepository : SpacetimeProviderRepository<MetadataDefinition, StdbMetadataDefinition>, IMetadataRepository
    {
        public SpacetimeMetadataRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbMetadataDefinition[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.MetadataDefinition.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override MetadataDefinition ToModel(StdbMetadataDefinition row) => new MetadataDefinition
        {
            Id = row.Id,
            Name = row.Name,
            Implementation = row.Implementation,
            ConfigContract = row.ConfigContract,
            Settings = DeserializeSettings(row.ConfigContract, row.SettingsJson),
            Enable = row.Enable,
            Tags = DeserializeTags(row.TagsJson),
            Message = DeserializeMessage(row.MessageJson)
        };

        protected override int GetRowId(StdbMetadataDefinition row) => row.Id;

        protected override void InvokeInsertReducer(MetadataDefinition model) => Conn.Connection.Reducers.InsertMetadataDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeUpdateReducer(MetadataDefinition model) => Conn.Connection.Reducers.UpdateMetadataDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteMetadataDefinition(id);
    }
}
