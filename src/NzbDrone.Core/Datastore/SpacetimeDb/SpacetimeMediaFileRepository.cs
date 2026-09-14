using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
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

        protected override RemoteTableHandle<EventContext, StdbEpisodeFile> Table => Conn.Connection.Db.EpisodeFile;

        protected override StdbEpisodeFile FindRowById(int id) => Conn.Connection.Db.EpisodeFile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int p3, string p4, long p5, SpacetimeDB.Timestamp p6, string p7, string p8, string p9, string p10, string p11, int p12, int p13, string p14, string p15, int p16)
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

            Conn.Connection.Reducers.OnUpdateEpisodeFile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateEpisodeFile -= Handler);
        }

        protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id)
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

            Conn.Connection.Reducers.OnDeleteEpisodeFile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteEpisodeFile -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int p1, int p2, string p3, long p4, SpacetimeDB.Timestamp p5, string p6, string p7, string p8, string p9, string p10, int p11, int p12, string p13, string p14, int p15)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted();
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnInsertEpisodeFile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertEpisodeFile -= Handler);
        }

        // Path is Ignore()'d in the SQL mapping (computed from RelativePath + root folder at read
        // time); Series/Episodes are LazyLoaded there, not persisted directly - same here.
        protected override EpisodeFile ToModel(StdbEpisodeFile row) => MapRow(row);

        /// <summary>
        /// Pure row-to-model mapping with no dependency on Conn or any other repository - exposed
        /// so a sibling repository's own ToModel (SpacetimeEpisodeRepository, populating its
        /// EpisodeFile) can read SpacetimeDB.Types.EpisodeFile rows straight off
        /// Conn.Connection.Db.EpisodeFile and map them directly, instead of re-entering this
        /// repository's own public async API (IMediaFileRepository.Find) from inside another
        /// repository's already-actor-thread ToModel call. That call previously worked only
        /// because Conn.RunOnActorAsync detects reentrancy from the same actor thread and runs
        /// inline rather than queuing/blocking - true, but an implicit dependency on that detail
        /// holding rather than an explicit one; reading the raw generated table handle directly
        /// (still safe here for exactly the same already-on-the-actor-thread reason) makes the
        /// data-access path explicit instead.
        /// </summary>
        internal static EpisodeFile MapRow(StdbEpisodeFile row) => new EpisodeFile
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

        public override Task MigrateInsert(EpisodeFile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertEpisodeFile(
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
            (int)model.ReleaseType));

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

        public Task<List<EpisodeFile>> GetFilesBySeries(int seriesId) =>
            Query(t => t.Iter().Where(r => r.SeriesId == seriesId).Select(ToModel).ToList());

        public async Task<List<EpisodeFile>> GetFilesBySeriesIds(List<int> seriesIds) =>
            (await All()).Where(c => seriesIds.Contains(c.SeriesId)).ToList();

        public Task<List<EpisodeFile>> GetFilesBySeason(int seriesId, int seasonNumber) =>
            Query(t => t.Iter().Where(r => r.SeriesId == seriesId && r.SeasonNumber == seasonNumber).Select(ToModel).ToList());

        public async Task<List<EpisodeFile>> GetFilesWithoutMediaInfo() => (await All()).Where(c => c.MediaInfo == null).ToList();

        public Task<List<EpisodeFile>> GetFilesWithRelativePath(int seriesId, string relativePath) =>
            Query(t => t.Iter().Where(r => r.SeriesId == seriesId && r.RelativePath == relativePath).Select(ToModel).ToList());

        public async Task DeleteForSeries(List<int> seriesIds)
        {
            foreach (var row in (await All()).Where(x => seriesIds.Contains(x.SeriesId)).ToList())
            {
                await Delete(row.Id);
            }
        }
    }
}
