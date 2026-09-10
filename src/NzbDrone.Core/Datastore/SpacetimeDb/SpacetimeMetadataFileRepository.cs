using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Messaging.Events;
using StdbMetadataFile = SpacetimeDB.Types.MetadataFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeMetadataFileRepository : SpacetimeExtraFileRepository<MetadataFile, StdbMetadataFile>, IMetadataFileRepository
    {
        public SpacetimeMetadataFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbMetadataFile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.MetadataFile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override MetadataFile ToModel(StdbMetadataFile row) => new MetadataFile
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            EpisodeFileId = row.EpisodeFileId,
            SeasonNumber = row.SeasonNumber,
            RelativePath = row.RelativePath,
            Added = SpacetimeDateTime.ToDateTime(row.Added),
            LastUpdated = SpacetimeDateTime.ToDateTime(row.LastUpdated),
            Extension = row.Extension,
            Hash = row.Hash,
            Consumer = row.Consumer,
            Type = (MetadataType)row.Type
        };

        protected override int GetRowId(StdbMetadataFile row) => row.Id;

        protected override void InvokeInsertReducer(MetadataFile model) => Conn.Connection.Reducers.InsertMetadataFile(
            model.SeriesId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeDateTime.ToTimestamp(model.LastUpdated),
            model.Extension ?? string.Empty,
            model.Hash ?? string.Empty,
            model.Consumer ?? string.Empty,
            (int)model.Type);

        protected override void InvokeUpdateReducer(MetadataFile model) => Conn.Connection.Reducers.UpdateMetadataFile(
            model.Id,
            model.SeriesId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeDateTime.ToTimestamp(model.LastUpdated),
            model.Extension ?? string.Empty,
            model.Hash ?? string.Empty,
            model.Consumer ?? string.Empty,
            (int)model.Type);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteMetadataFile(id);
    }
}
