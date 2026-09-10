using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Messaging.Events;
using StdbOtherExtraFile = SpacetimeDB.Types.OtherExtraFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeOtherExtraFileRepository : SpacetimeExtraFileRepository<OtherExtraFile, StdbOtherExtraFile>, IOtherExtraFileRepository
    {
        public SpacetimeOtherExtraFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbOtherExtraFile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.OtherExtraFile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override OtherExtraFile ToModel(StdbOtherExtraFile row) => new OtherExtraFile
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            EpisodeFileId = row.EpisodeFileId,
            SeasonNumber = row.SeasonNumber,
            RelativePath = row.RelativePath,
            Added = SpacetimeDateTime.ToDateTime(row.Added),
            LastUpdated = SpacetimeDateTime.ToDateTime(row.LastUpdated),
            Extension = row.Extension
        };

        protected override int GetRowId(StdbOtherExtraFile row) => row.Id;

        protected override void InvokeInsertReducer(OtherExtraFile model) => Conn.Connection.Reducers.InsertOtherExtraFile(
            model.SeriesId, model.EpisodeFileId, model.SeasonNumber, model.RelativePath ?? string.Empty, SpacetimeDateTime.ToTimestamp(model.Added), SpacetimeDateTime.ToTimestamp(model.LastUpdated), model.Extension ?? string.Empty);

        protected override void InvokeUpdateReducer(OtherExtraFile model) => Conn.Connection.Reducers.UpdateOtherExtraFile(
            model.Id, model.SeriesId, model.EpisodeFileId, model.SeasonNumber, model.RelativePath ?? string.Empty, SpacetimeDateTime.ToTimestamp(model.Added), SpacetimeDateTime.ToTimestamp(model.LastUpdated), model.Extension ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteOtherExtraFile(id);
    }
}
