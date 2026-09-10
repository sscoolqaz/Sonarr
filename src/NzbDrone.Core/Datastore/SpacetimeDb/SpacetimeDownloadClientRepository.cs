using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using StdbDownloadClientDefinition = SpacetimeDB.Types.DownloadClientDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDownloadClientRepository : SpacetimeProviderRepository<DownloadClientDefinition, StdbDownloadClientDefinition>, IDownloadClientRepository
    {
        public SpacetimeDownloadClientRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbDownloadClientDefinition[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.DownloadClientDefinition.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override DownloadClientDefinition ToModel(StdbDownloadClientDefinition row) => new DownloadClientDefinition
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

        protected override int GetRowId(StdbDownloadClientDefinition row) => row.Id;

        protected override void InvokeInsertReducer(DownloadClientDefinition model) => Conn.Connection.Reducers.InsertDownloadClientDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeUpdateReducer(DownloadClientDefinition model) => Conn.Connection.Reducers.UpdateDownloadClientDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteDownloadClientDefinition(id);
    }
}
