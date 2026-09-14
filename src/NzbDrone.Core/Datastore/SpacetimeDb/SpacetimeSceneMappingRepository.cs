using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbSceneMapping = SpacetimeDB.Types.SceneMapping;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeSceneMappingRepository : SpacetimeBasicRepository<SceneMapping, StdbSceneMapping>, ISceneMappingRepository
    {
        public SpacetimeSceneMappingRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbSceneMapping> Table => Conn.Connection.Db.SceneMapping;

        protected override StdbSceneMapping FindRowById(int id) => Conn.Connection.Db.SceneMapping.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4, string p5, int p6, int? p7, int? p8, string p9, int? p10, string p11, string p12, string p13)
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

            Conn.Connection.Reducers.OnUpdateSceneMapping += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateSceneMapping -= Handler);
        }

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

        public List<SceneMapping> FindByTvdbid(int tvdbId) =>
            Query(t => t.Iter().Where(r => r.TvdbId == tvdbId).Select(ToModel).ToList());

        public List<SceneMapping> GetAllByType(string type) =>
            Query(t => t.Iter().Where(r => r.Type == type).Select(ToModel).ToList());
    }
}
