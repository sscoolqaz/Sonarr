using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using StdbNotificationDefinition = SpacetimeDB.Types.NotificationDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeNotificationRepository : SpacetimeProviderRepository<NotificationDefinition, StdbNotificationDefinition>, INotificationRepository
    {
        public SpacetimeNotificationRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbNotificationDefinition[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.NotificationDefinition.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override NotificationDefinition ToModel(StdbNotificationDefinition row) => new NotificationDefinition
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

        protected override int GetRowId(StdbNotificationDefinition row) => row.Id;

        protected override void InvokeInsertReducer(NotificationDefinition model) => Conn.Connection.Reducers.InsertNotificationDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        public override void MigrateInsert(NotificationDefinition model) => Conn.Connection.Reducers.MigrateInsertNotificationDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeUpdateReducer(NotificationDefinition model) => Conn.Connection.Reducers.UpdateNotificationDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteNotificationDefinition(id);

        public void UpdateSettings(NotificationDefinition model) => SetFields(model, m => m.Settings);
    }
}
