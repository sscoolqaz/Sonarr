using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ImportLists.ImportListItems;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using StdbImportListItem = SpacetimeDB.Types.ImportListItem;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeImportListItemRepository : SpacetimeBasicRepository<ImportListItemInfo, StdbImportListItem>, IImportListItemRepository
    {
        public SpacetimeImportListItemRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbImportListItem[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.ImportListItem.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

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

        public List<ImportListItemInfo> GetAllForLists(List<int> listIds) =>
            All().Where(x => listIds.Contains(x.ImportListId)).ToList();
    }
}
