using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Messaging.Events;
using StdbSceneMapping = SpacetimeDB.Types.SceneMapping;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeSceneMappingRepository : SpacetimeBasicRepository<SceneMapping, StdbSceneMapping>, ISceneMappingRepository
    {
        public SpacetimeSceneMappingRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbSceneMapping[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.SceneMapping.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override SceneMapping ToModel(StdbSceneMapping row) => new SceneMapping
        {
            Id = row.Id,
            MappingId = row.MappingId,
            Title = row.Title,
            ParseTerm = row.ParseTerm,
            SearchTerm = row.SearchTerm,
            TvdbId = row.TvdbId,
            SeasonNumber = row.SeasonNumber,
            SceneSeasonNumber = row.SceneSeasonNumber,
            SceneOrigin = row.SceneOrigin,
            SearchMode = row.SearchMode.HasValue ? (SearchMode)row.SearchMode.Value : (SearchMode?)null,
            Comment = row.Comment,
            FilterRegex = row.FilterRegex,
            Type = row.Type
        };

        protected override int GetRowId(StdbSceneMapping row) => row.Id;

        protected override void InvokeInsertReducer(SceneMapping model) => Conn.Connection.Reducers.InsertSceneMapping(
            model.MappingId ?? string.Empty,
            model.Title ?? string.Empty,
            model.ParseTerm ?? string.Empty,
            model.SearchTerm ?? string.Empty,
            model.TvdbId,
            model.SeasonNumber,
            model.SceneSeasonNumber,
            model.SceneOrigin ?? string.Empty,
            (int?)model.SearchMode,
            model.Comment ?? string.Empty,
            model.FilterRegex ?? string.Empty,
            model.Type ?? string.Empty);

        protected override void InvokeUpdateReducer(SceneMapping model) => Conn.Connection.Reducers.UpdateSceneMapping(
            model.Id,
            model.MappingId ?? string.Empty,
            model.Title ?? string.Empty,
            model.ParseTerm ?? string.Empty,
            model.SearchTerm ?? string.Empty,
            model.TvdbId,
            model.SeasonNumber,
            model.SceneSeasonNumber,
            model.SceneOrigin ?? string.Empty,
            (int?)model.SearchMode,
            model.Comment ?? string.Empty,
            model.FilterRegex ?? string.Empty,
            model.Type ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteSceneMapping(id);

        public List<SceneMapping> FindByTvdbid(int tvdbId) => RemoteQuery($"WHERE TvdbId = {tvdbId}").Select(ToModel).ToList();

        public List<SceneMapping> GetAllByType(string type) => RemoteQuery($"WHERE Type = '{EscapeSqlString(type)}'").Select(ToModel).ToList();
    }
}
