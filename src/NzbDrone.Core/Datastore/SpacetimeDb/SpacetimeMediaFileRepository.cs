using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using StdbEpisodeFile = SpacetimeDB.Types.EpisodeFile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// QualityId is the Phase 3-designed normalized column backing the quality-rank lookup
    /// (replacing the JSON-extract/LIKE hack the SQL repository needs). Per the adversarial
    /// review that caught the missing invariant: QualityId must be derived from the submitted
    /// Quality document at exactly one choke point, never accepted independently from a caller.
    /// That choke point is here - InvokeInsertReducer/InvokeUpdateReducer always derive it from
    /// model.Quality themselves, and SpacetimeBasicRepository's Insert/Update/SetFields are the
    /// only paths that ever call them, so there is no way to write an EpisodeFile row through
    /// this repository with a QualityId that doesn't match its own embedded Quality document.
    /// </summary>
    public class SpacetimeMediaFileRepository : SpacetimeBasicRepository<EpisodeFile, StdbEpisodeFile>, IMediaFileRepository
    {
        public SpacetimeMediaFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbEpisodeFile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.EpisodeFile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        // Path is Ignore()'d in the SQL mapping (computed from RelativePath + root folder at read
        // time); Series/Episodes are LazyLoaded there, not persisted directly - same here.
        protected override EpisodeFile ToModel(StdbEpisodeFile row) => new EpisodeFile
        {
            Id = row.Id,
            SeriesId = row.SeriesId,
            SeasonNumber = row.SeasonNumber,
            RelativePath = row.RelativePath,
            Size = row.Size,
            DateAdded = SpacetimeDateTime.ToDateTime(row.DateAdded),
            OriginalFilePath = row.OriginalFilePath,
            SceneName = row.SceneName,
            ReleaseGroup = row.ReleaseGroup,
            ReleaseHash = row.ReleaseHash,
            Quality = SpacetimeJson.Deserialize<QualityModel>(row.QualityJson),
            IndexerFlags = (IndexerFlags)row.IndexerFlags,
            MediaInfo = SpacetimeJson.Deserialize<MediaInfoModel>(row.MediaInfoJson),
            Languages = SpacetimeJson.Deserialize<List<Language>>(row.LanguagesJson) ?? new List<Language>(),
            ReleaseType = (ReleaseType)row.ReleaseType
        };

        protected override int GetRowId(StdbEpisodeFile row) => row.Id;

        private static int DeriveQualityId(EpisodeFile model) => model.Quality?.Quality?.Id ?? 0;

        public override void MigrateInsert(EpisodeFile model) => Conn.Connection.Reducers.MigrateInsertEpisodeFile(
            model.Id,
            model.SeriesId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            model.Size,
            SpacetimeDateTime.ToTimestamp(model.DateAdded),
            model.OriginalFilePath ?? string.Empty,
            model.SceneName ?? string.Empty,
            model.ReleaseGroup ?? string.Empty,
            model.ReleaseHash ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            (int)model.IndexerFlags,
            SpacetimeJson.Serialize(model.MediaInfo),
            SpacetimeJson.Serialize(model.Languages),
            (int)model.ReleaseType);

        protected override void InvokeInsertReducer(EpisodeFile model) => Conn.Connection.Reducers.InsertEpisodeFile(
            model.SeriesId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            model.Size,
            SpacetimeDateTime.ToTimestamp(model.DateAdded),
            model.OriginalFilePath ?? string.Empty,
            model.SceneName ?? string.Empty,
            model.ReleaseGroup ?? string.Empty,
            model.ReleaseHash ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            (int)model.IndexerFlags,
            SpacetimeJson.Serialize(model.MediaInfo),
            SpacetimeJson.Serialize(model.Languages),
            (int)model.ReleaseType);

        protected override void InvokeUpdateReducer(EpisodeFile model) => Conn.Connection.Reducers.UpdateEpisodeFile(
            model.Id,
            model.SeriesId,
            model.SeasonNumber,
            model.RelativePath ?? string.Empty,
            model.Size,
            SpacetimeDateTime.ToTimestamp(model.DateAdded),
            model.OriginalFilePath ?? string.Empty,
            model.SceneName ?? string.Empty,
            model.ReleaseGroup ?? string.Empty,
            model.ReleaseHash ?? string.Empty,
            SpacetimeJson.Serialize(model.Quality),
            DeriveQualityId(model),
            (int)model.IndexerFlags,
            SpacetimeJson.Serialize(model.MediaInfo),
            SpacetimeJson.Serialize(model.Languages),
            (int)model.ReleaseType);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteEpisodeFile(id);

        public List<EpisodeFile> GetFilesBySeries(int seriesId) => RemoteQuery($"WHERE SeriesId = {seriesId}").Select(ToModel).ToList();

        public List<EpisodeFile> GetFilesBySeriesIds(List<int> seriesIds) =>
            All().Where(c => seriesIds.Contains(c.SeriesId)).ToList();

        public List<EpisodeFile> GetFilesBySeason(int seriesId, int seasonNumber) =>
            RemoteQuery($"WHERE SeriesId = {seriesId} AND SeasonNumber = {seasonNumber}").Select(ToModel).ToList();

        // The one query in this entity that's a real IS NULL predicate, which SpacetimeDB's WHERE
        // grammar doesn't support at all - fetch-all and filter client-side (Tier 1 table sizes,
        // maintenance-shaped query, not a hot path - see the Phase 3 schema design's note on this
        // exact method).
        public List<EpisodeFile> GetFilesWithoutMediaInfo() => All().Where(c => c.MediaInfo == null).ToList();

        public List<EpisodeFile> GetFilesWithRelativePath(int seriesId, string relativePath) =>
            RemoteQuery($"WHERE SeriesId = {seriesId} AND RelativePath = '{EscapeSqlString(relativePath)}'").Select(ToModel).ToList();

        public void DeleteForSeries(List<int> seriesIds)
        {
            foreach (var row in All().Where(x => seriesIds.Contains(x.SeriesId)).ToList())
            {
                Delete(row.Id);
            }
        }
    }
}
