using System;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbNamingConfig = SpacetimeDB.Types.NamingConfig;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeNamingConfigRepository : SpacetimeBasicRepository<NamingConfig, StdbNamingConfig>, INamingConfigRepository
    {
        public SpacetimeNamingConfigRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbNamingConfig> Table => Conn.Connection.Db.NamingConfig;

        protected override StdbNamingConfig FindRowById(int id) => Conn.Connection.Db.NamingConfig.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, bool p2, bool p3, int p4, string p5, int p6, string p7, string p8, string p9, string p10, string p11, string p12)
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

            Conn.Connection.Reducers.OnUpdateNamingConfig += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateNamingConfig -= Handler);
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

            Conn.Connection.Reducers.OnDeleteNamingConfig += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteNamingConfig -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, bool p1, bool p2, int p3, string p4, int p5, string p6, string p7, string p8, string p9, string p10, string p11)
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

            Conn.Connection.Reducers.OnInsertNamingConfig += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertNamingConfig -= Handler);
        }

        protected override NamingConfig ToModel(StdbNamingConfig row) => new NamingConfig
        {
            Id = row.Id,
            RenameEpisodes = row.RenameEpisodes,
            ReplaceIllegalCharacters = row.ReplaceIllegalCharacters,
            ColonReplacementFormat = (ColonReplacementFormat)row.ColonReplacementFormat,
            CustomColonReplacementFormat = row.CustomColonReplacementFormat,
            MultiEpisodeStyle = (MultiEpisodeStyle)row.MultiEpisodeStyle,
            StandardEpisodeFormat = row.StandardEpisodeFormat,
            DailyEpisodeFormat = row.DailyEpisodeFormat,
            AnimeEpisodeFormat = row.AnimeEpisodeFormat,
            SeriesFolderFormat = row.SeriesFolderFormat,
            SeasonFolderFormat = row.SeasonFolderFormat,
            SpecialsFolderFormat = row.SpecialsFolderFormat
        };

        protected override int GetRowId(StdbNamingConfig row) => row.Id;

        protected override void InvokeInsertReducer(NamingConfig model) => Conn.Connection.Reducers.InsertNamingConfig(
            model.RenameEpisodes,
            model.ReplaceIllegalCharacters,
            (int)model.ColonReplacementFormat,
            model.CustomColonReplacementFormat ?? string.Empty,
            (int)model.MultiEpisodeStyle,
            model.StandardEpisodeFormat ?? string.Empty,
            model.DailyEpisodeFormat ?? string.Empty,
            model.AnimeEpisodeFormat ?? string.Empty,
            model.SeriesFolderFormat ?? string.Empty,
            model.SeasonFolderFormat ?? string.Empty,
            model.SpecialsFolderFormat ?? string.Empty);

        public override Task MigrateInsert(NamingConfig model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertNamingConfig(
            model.Id,
            model.RenameEpisodes,
            model.ReplaceIllegalCharacters,
            (int)model.ColonReplacementFormat,
            model.CustomColonReplacementFormat ?? string.Empty,
            (int)model.MultiEpisodeStyle,
            model.StandardEpisodeFormat ?? string.Empty,
            model.DailyEpisodeFormat ?? string.Empty,
            model.AnimeEpisodeFormat ?? string.Empty,
            model.SeriesFolderFormat ?? string.Empty,
            model.SeasonFolderFormat ?? string.Empty,
            model.SpecialsFolderFormat ?? string.Empty));

        protected override void InvokeUpdateReducer(NamingConfig model) => Conn.Connection.Reducers.UpdateNamingConfig(
            model.Id,
            model.RenameEpisodes,
            model.ReplaceIllegalCharacters,
            (int)model.ColonReplacementFormat,
            model.CustomColonReplacementFormat ?? string.Empty,
            (int)model.MultiEpisodeStyle,
            model.StandardEpisodeFormat ?? string.Empty,
            model.DailyEpisodeFormat ?? string.Empty,
            model.AnimeEpisodeFormat ?? string.Empty,
            model.SeriesFolderFormat ?? string.Empty,
            model.SeasonFolderFormat ?? string.Empty,
            model.SpecialsFolderFormat ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteNamingConfig(id);
    }
}
