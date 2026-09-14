using System;
using System.Threading.Tasks;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbOtherExtraFile = SpacetimeDB.Types.OtherExtraFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeOtherExtraFileRepository : SpacetimeExtraFileRepository<OtherExtraFile, StdbOtherExtraFile>, IOtherExtraFileRepository
    {
        public SpacetimeOtherExtraFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbOtherExtraFile> Table => Conn.Connection.Db.OtherExtraFile;

        protected override StdbOtherExtraFile FindRowById(int id) => Conn.Connection.Db.OtherExtraFile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int? p3, int? p4, string p5, SpacetimeDB.Timestamp p6, SpacetimeDB.Timestamp p7, string p8)
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

            Conn.Connection.Reducers.OnUpdateOtherExtraFile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateOtherExtraFile -= Handler);
        }

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

        public override Task MigrateInsert(OtherExtraFile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertOtherExtraFile(
            model.Id, model.SeriesId, model.EpisodeFileId, model.SeasonNumber, model.RelativePath ?? string.Empty, SpacetimeDateTime.ToTimestamp(model.Added), SpacetimeDateTime.ToTimestamp(model.LastUpdated), model.Extension ?? string.Empty));

        protected override void InvokeUpdateReducer(OtherExtraFile model) => Conn.Connection.Reducers.UpdateOtherExtraFile(
            model.Id, model.SeriesId, model.EpisodeFileId, model.SeasonNumber, model.RelativePath ?? string.Empty, SpacetimeDateTime.ToTimestamp(model.Added), SpacetimeDateTime.ToTimestamp(model.LastUpdated), model.Extension ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteOtherExtraFile(id);
    }
}
