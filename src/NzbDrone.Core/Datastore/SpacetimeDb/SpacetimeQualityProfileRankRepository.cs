using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbQualityProfileQualityRank = SpacetimeDB.Types.QualityProfileQualityRank;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeQualityProfileRankRepository : SpacetimeBasicRepository<QualityProfileQualityRank, StdbQualityProfileQualityRank>, IQualityProfileRankRepository
    {
        public SpacetimeQualityProfileRankRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbQualityProfileQualityRank> Table => Conn.Connection.Db.QualityProfileQualityRank;

        protected override StdbQualityProfileQualityRank FindRowById(int id) => Conn.Connection.Db.QualityProfileQualityRank.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, int p2, int p3, double p4)
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

            Conn.Connection.Reducers.OnUpdateQualityProfileQualityRank += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateQualityProfileQualityRank -= Handler);
        }

        protected override QualityProfileQualityRank ToModel(StdbQualityProfileQualityRank row) => new QualityProfileQualityRank
        {
            Id = row.Id,
            ProfileId = row.ProfileId,
            QualityId = row.QualityId,
            Score = row.Score
        };

        protected override int GetRowId(StdbQualityProfileQualityRank row) => row.Id;

        protected override void InvokeInsertReducer(QualityProfileQualityRank model) =>
            Conn.Connection.Reducers.InsertQualityProfileQualityRank(model.ProfileId, model.QualityId, model.Score);

        protected override void InvokeUpdateReducer(QualityProfileQualityRank model) =>
            Conn.Connection.Reducers.UpdateQualityProfileQualityRank(model.Id, model.ProfileId, model.QualityId, model.Score);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteQualityProfileQualityRank(id);

        public void ReplaceForProfile(int profileId, IEnumerable<QualityProfileQualityRank> ranks)
        {
            var input = ranks.Select(r => new SpacetimeDB.Types.QualityRankInput { QualityId = r.QualityId, Score = r.Score }).ToList();

            Conn.Connection.Reducers.ReplaceQualityProfileQualityRanks(profileId, input);
        }

        public void DeleteForProfile(int profileId)
        {
            var rows = Query(t => t.Iter().Where(r => r.ProfileId == profileId).Select(ToModel).ToList());

            foreach (var row in rows)
            {
                Delete(row.Id);
            }
        }
    }
}
