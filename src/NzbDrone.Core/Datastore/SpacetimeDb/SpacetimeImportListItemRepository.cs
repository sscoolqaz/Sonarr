using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbImportListItem = SpacetimeDB.Types.ImportListItem;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeImportListItemRepository : SpacetimeBasicRepository<ImportListItemInfo, StdbImportListItem>, IImportListItemRepository
    {
        public SpacetimeImportListItemRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbImportListItem> Table => Conn.Connection.Db.ImportListItem;

        protected override StdbImportListItem FindRowById(int id) => Conn.Connection.Db.ImportListItem.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, string p3, int p4, int p5, int p6, string p7, int p8, int p9, SpacetimeDB.Timestamp p10)
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

            Conn.Connection.Reducers.OnUpdateImportListItem += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateImportListItem -= Handler);
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

            Conn.Connection.Reducers.OnDeleteImportListItem += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteImportListItem -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int p1, string p2, int p3, int p4, int p5, string p6, int p7, int p8, SpacetimeDB.Timestamp p9)
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

            Conn.Connection.Reducers.OnInsertImportListItem += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertImportListItem -= Handler);
        }

        // ImportList/Seasons are Ignore()'d in the SQL mapping too - not persisted here either.
        protected override ImportListItemInfo ToModel(StdbImportListItem row) => new ImportListItemInfo
        {
            Id = row.Id,
            ImportListId = row.ImportListId,
            Title = row.Title,
            Year = row.Year,
            TvdbId = row.TvdbId,
            TmdbId = row.TmdbId,
            ImdbId = row.ImdbId,
            MalId = row.MalId,
            AniListId = row.AniListId,
            ReleaseDate = SpacetimeDateTime.ToDateTime(row.ReleaseDate)
        };

        protected override int GetRowId(StdbImportListItem row) => row.Id;

        protected override void InvokeInsertReducer(ImportListItemInfo model) => Conn.Connection.Reducers.InsertImportListItem(
            model.ImportListId, model.Title ?? string.Empty, model.Year, model.TvdbId, model.TmdbId, model.ImdbId ?? string.Empty, model.MalId, model.AniListId, SpacetimeDateTime.ToTimestamp(model.ReleaseDate));

        protected override void InvokeUpdateReducer(ImportListItemInfo model) => Conn.Connection.Reducers.UpdateImportListItem(
            model.Id, model.ImportListId, model.Title ?? string.Empty, model.Year, model.TvdbId, model.TmdbId, model.ImdbId ?? string.Empty, model.MalId, model.AniListId, SpacetimeDateTime.ToTimestamp(model.ReleaseDate));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteImportListItem(id);

        public async Task<List<ImportListItemInfo>> GetAllForLists(List<int> listIds) =>
            (await All()).Where(x => listIds.Contains(x.ImportListId)).ToList();
    }
}
