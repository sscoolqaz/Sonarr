using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Qualities;
using StdbQualityProfileQualityRank = SpacetimeDB.Types.QualityProfileQualityRank;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeQualityProfileRankRepository : SpacetimeBasicRepository<QualityProfileQualityRank, StdbQualityProfileQualityRank>, IQualityProfileRankRepository
    {
        public SpacetimeQualityProfileRankRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbQualityProfileQualityRank[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.QualityProfileQualityRank.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

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
            foreach (var row in RemoteQuery($"WHERE ProfileId = {profileId}").Select(ToModel).ToList())
            {
                Delete(row.Id);
            }
        }
    }
}
