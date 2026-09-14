using System;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbMetadataFile = SpacetimeDB.Types.MetadataFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeMetadataFileRepository : SpacetimeExtraFileRepository<MetadataFile, StdbMetadataFile>, IMetadataFileRepository
    {
        public SpacetimeMetadataFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbMetadataFile> Table => Conn.Connection.Db.MetadataFile;

        protected override StdbMetadataFile FindRowById(int id) => Conn.Connection.Db.MetadataFile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int? p3, int? p4, string p5, SpacetimeDB.Timestamp p6, SpacetimeDB.Timestamp p7, string p8, string p9, string p10, int p11)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateMetadataFile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateMetadataFile -= Handler);
        }

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

        public override void MigrateInsert(MetadataFile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertMetadataFile(
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
            (int)model.Type));

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
