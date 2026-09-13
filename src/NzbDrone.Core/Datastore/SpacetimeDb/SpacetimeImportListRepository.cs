using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Messaging.Events;
using StdbImportListDefinition = SpacetimeDB.Types.ImportListDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeImportListRepository : SpacetimeProviderRepository<ImportListDefinition, StdbImportListDefinition>, IImportListRepository
    {
        public SpacetimeImportListRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbImportListDefinition[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.ImportListDefinition.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override ImportListDefinition ToModel(StdbImportListDefinition row) => new ImportListDefinition
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

        protected override int GetRowId(StdbImportListDefinition row) => row.Id;

        protected override void InvokeInsertReducer(ImportListDefinition model) => Conn.Connection.Reducers.InsertImportListDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        public override void MigrateInsert(ImportListDefinition model) => Conn.Connection.Reducers.MigrateInsertImportListDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeUpdateReducer(ImportListDefinition model) => Conn.Connection.Reducers.UpdateImportListDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteImportListDefinition(id);

        public void UpdateSettings(ImportListDefinition model) => SetFields(model, m => m.Settings);
    }
}
