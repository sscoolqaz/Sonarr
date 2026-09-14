using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbQualityProfile = SpacetimeDB.Types.QualityProfile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeQualityProfileRepository : SpacetimeBasicRepository<QualityProfile, StdbQualityProfile>, IQualityProfileRepository
    {
        // FormatItems is stored as {format:<CustomFormat.Id>, score} pairs, not the full
        // CustomFormat object - mirrors CustomFormatIntConverter's storage shape exactly (the
        // real repository's Query() override rehydrates the full object from a direct
        // Conn.Connection.Db.CustomFormat table read in MapRow below, and skips formats that
        // were since removed).
        private struct FormatItemDto
        {
            public int Format { get; set; }
            public int Score { get; set; }
        }

        private static readonly JsonSerializerOptions SerializerSettings = SpacetimeEmbeddedJson.Create();

        public SpacetimeQualityProfileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbQualityProfile> Table => Conn.Connection.Db.QualityProfile;

        protected override StdbQualityProfile FindRowById(int id) => Conn.Connection.Db.QualityProfile.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, bool p3, int p4, int p5, int p6, int p7, string p8, string p9)
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

            Conn.Connection.Reducers.OnUpdateQualityProfile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateQualityProfile -= Handler);
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

            Conn.Connection.Reducers.OnDeleteQualityProfile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteQualityProfile -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, string p1, bool p2, int p3, int p4, int p5, int p6, string p7, string p8)
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

            Conn.Connection.Reducers.OnInsertQualityProfile += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertQualityProfile -= Handler);
        }

        protected override QualityProfile ToModel(StdbQualityProfile row) => MapRow(row, Conn);

        /// <summary>
        /// Row-to-model mapping that only depends on the given connection's Db (via
        /// Conn.Connection.Db.CustomFormat), not on this repository instance or any other
        /// repository/service - exposed so a sibling repository's own ToModel (e.g.
        /// SpacetimeSeriesRepository, populating its LazyLoaded&lt;QualityProfile&gt;) can read a
        /// SpacetimeDB.Types.QualityProfile row straight off Conn.Connection.Db.QualityProfile
        /// and map it directly. This used to go through ICustomFormatService.All() (which itself
        /// resolves through ICustomFormatRepository -> Conn.RunOnActorAsync) and, from Series,
        /// through this repository's own public IQualityProfileRepository.Find - both only worked
        /// because Conn.RunOnActorAsync detects it's already running on the actor thread and
        /// completes synchronously instead of queuing/blocking, which is real but an implicit
        /// dependency on that reentrancy detail rather than an explicit one. Reading
        /// Conn.Connection.Db.CustomFormat.Iter() directly here (still safe only because every
        /// caller of this method - this class's own ToModel, and SpacetimeSeriesRepository's - is
        /// itself always invoked from inside an already-actor-thread RunOnActorAsync closure, per
        /// SpacetimeBasicRepository.Query/All) makes the data-access path explicit instead of
        /// leaning on that reentrancy holding.
        /// </summary>
        internal static QualityProfile MapRow(StdbQualityProfile row, ISpacetimeDbConnection conn)
        {
            var cfs = conn.Connection.Db.CustomFormat.Iter().Select(SpacetimeCustomFormatRepository.MapRow).ToDictionary(c => c.Id);
            var dtos = JsonSerializer.Deserialize<List<FormatItemDto>>(row.FormatItemsJson, SerializerSettings) ?? new List<FormatItemDto>();
            var formatItems = new List<ProfileFormatItem>();

            foreach (var dto in dtos)
            {
                if (cfs.TryGetValue(dto.Format, out var format))
                {
                    formatItems.Add(new ProfileFormatItem { Format = format, Score = dto.Score });
                }
            }

            return new QualityProfile
            {
                Id = row.Id,
                Name = row.Name,
                UpgradeAllowed = row.UpgradeAllowed,
                Cutoff = row.Cutoff,
                MinFormatScore = row.MinFormatScore,
                CutoffFormatScore = row.CutoffFormatScore,
                MinUpgradeFormatScore = row.MinUpgradeFormatScore,
                FormatItems = formatItems,
                Items = JsonSerializer.Deserialize<List<QualityProfileQualityItem>>(row.ItemsJson, SerializerSettings) ?? new List<QualityProfileQualityItem>()
            };
        }

        protected override int GetRowId(StdbQualityProfile row) => row.Id;

        // ToModel/MapRow silently drops any FormatItemDto whose Format id no longer exists in
        // the CustomFormat table (see the loop above) - so profile.FormatItems as returned by
        // All()/Find() can never reveal a stale id for SpacetimeCleanupQualityProfileFormatItems
        // to detect and persist a removal for. This gives that task the raw, unfiltered ids
        // exactly as stored, bypassing the CustomFormat existence check.
        public Task<Dictionary<int, List<int>>> GetRawFormatItemIds()
        {
            return Query(t => t.Iter().ToDictionary(
                row => row.Id,
                row => (JsonSerializer.Deserialize<List<FormatItemDto>>(row.FormatItemsJson, SerializerSettings) ?? new List<FormatItemDto>())
                    .Select(dto => dto.Format)
                    .ToList()));
        }

        private static string SerializeFormatItems(QualityProfile model) =>
            JsonSerializer.Serialize(model.FormatItems.Select(f => new FormatItemDto { Format = f.Format.Id, Score = f.Score }).ToList(), SerializerSettings);

        private static string SerializeItems(QualityProfile model) =>
            JsonSerializer.Serialize(model.Items, SerializerSettings);

        protected override void InvokeInsertReducer(QualityProfile model) => Conn.Connection.Reducers.InsertQualityProfile(
            model.Name ?? string.Empty,
            model.UpgradeAllowed,
            model.Cutoff,
            model.MinFormatScore,
            model.CutoffFormatScore,
            model.MinUpgradeFormatScore,
            SerializeFormatItems(model),
            SerializeItems(model));

        public override Task MigrateInsert(QualityProfile model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertQualityProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.UpgradeAllowed,
            model.Cutoff,
            model.MinFormatScore,
            model.CutoffFormatScore,
            model.MinUpgradeFormatScore,
            SerializeFormatItems(model),
            SerializeItems(model)));

        protected override void InvokeUpdateReducer(QualityProfile model) => Conn.Connection.Reducers.UpdateQualityProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.UpgradeAllowed,
            model.Cutoff,
            model.MinFormatScore,
            model.CutoffFormatScore,
            model.MinUpgradeFormatScore,
            SerializeFormatItems(model),
            SerializeItems(model));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteQualityProfile(id);

        public async Task<bool> Exists(int id) => await Find(id) != null;
    }
}
