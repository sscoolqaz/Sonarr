using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using StdbQualityDefinition = SpacetimeDB.Types.QualityDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeQualityDefinitionRepository : SpacetimeBasicRepository<QualityDefinition, StdbQualityDefinition>, IQualityDefinitionRepository
    {
        public SpacetimeQualityDefinitionRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbQualityDefinition[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.QualityDefinition.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        // GroupName/Weight/MinSize/MaxSize/PreferredSize are Ignore()'d in the SQL mapping too -
        // not persisted here either, left at their C# defaults same as the SQL path.
        protected override QualityDefinition ToModel(StdbQualityDefinition row) => new QualityDefinition
        {
            Id = row.Id,
            Quality = SpacetimeJson.Deserialize<Quality>(row.QualityJson),
            Title = row.Title
        };

        protected override int GetRowId(StdbQualityDefinition row) => row.Id;

        protected override void InvokeInsertReducer(QualityDefinition model) =>
            Conn.Connection.Reducers.InsertQualityDefinition(SpacetimeJson.Serialize(model.Quality), model.Title ?? string.Empty);

        protected override void InvokeUpdateReducer(QualityDefinition model) =>
            Conn.Connection.Reducers.UpdateQualityDefinition(model.Id, SpacetimeJson.Serialize(model.Quality), model.Title ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteQualityDefinition(id);
    }
}
