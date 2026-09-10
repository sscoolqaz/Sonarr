using System.Collections.Generic;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using StdbSubtitleFile = SpacetimeDB.Types.SubtitleFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeSubtitleFileRepository : SpacetimeExtraFileRepository<SubtitleFile, StdbSubtitleFile>, ISubtitleFileRepository
    {
        public SpacetimeSubtitleFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbSubtitleFile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.SubtitleFile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

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
