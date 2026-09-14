using System;
using System.Collections.Generic;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbSubtitleFile = SpacetimeDB.Types.SubtitleFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeSubtitleFileRepository : SpacetimeExtraFileRepository<SubtitleFile, StdbSubtitleFile>, ISubtitleFileRepository
    {
        public SpacetimeSubtitleFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbSubtitleFile> Table => Conn.Connection.Db.SubtitleFile;

        protected override StdbSubtitleFile FindRowById(int id) => Conn.Connection.Db.SubtitleFile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int? p3, int? p4, string p5, SpacetimeDB.Timestamp p6, SpacetimeDB.Timestamp p7, string p8, string p9, int p10, string p11, string p12)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateSubtitleFile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateSubtitleFile -= Handler);
        }

        protected override SubtitleFile ToModel(StdbSubtitleFile row) => new SubtitleFile
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            EpisodeFileId = row.EpisodeFileId,
            SeasonNumber = row.SeasonNumber,
            RelativePath = row.RelativePath,
            Added = SpacetimeDateTime.ToDateTime(row.Added),
            LastUpdated = SpacetimeDateTime.ToDateTime(row.LastUpdated),
            Extension = row.Extension,
            Language = SpacetimeJson.Deserialize<Language>(row.LanguageJson),
            Copy = row.Copy,
            LanguageTags = SpacetimeJson.Deserialize<List<string>>(row.LanguageTagsJson) ?? new List<string>(),
            Title = row.Title
        };

        protected override int GetRowId(StdbSubtitleFile row) => row.Id;

        protected override void InvokeInsertReducer(SubtitleFile model) => Conn.Connection.Reducers.InsertSubtitleFile(
            model.SeriesId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeDateTime.ToTimestamp(model.LastUpdated),
            model.Extension ?? string.Empty,
            SpacetimeJson.Serialize(model.Language),
            model.Copy,
            SpacetimeJson.Serialize(model.LanguageTags),
            model.Title ?? string.Empty);

        public override void MigrateInsert(SubtitleFile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertSubtitleFile(
            model.Id,
            model.SeriesId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeDateTime.ToTimestamp(model.LastUpdated),
            model.Extension ?? string.Empty,
            SpacetimeJson.Serialize(model.Language),
            model.Copy,
            SpacetimeJson.Serialize(model.LanguageTags),
            model.Title ?? string.Empty));

        protected override void InvokeUpdateReducer(SubtitleFile model) => Conn.Connection.Reducers.UpdateSubtitleFile(
            model.Id,
            model.SeriesId,
            model.EpisodeFileId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            SpacetimeDateTime.ToTimestamp(model.Added),
            SpacetimeDateTime.ToTimestamp(model.LastUpdated),
            model.Extension ?? string.Empty,
            SpacetimeJson.Serialize(model.Language),
            model.Copy,
            SpacetimeJson.Serialize(model.LanguageTags),
            model.Title ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteSubtitleFile(id);
    }
}
