using NzbDrone.Core.CustomFilters;
using NzbDrone.Core.Messaging.Events;
using StdbCustomFilter = SpacetimeDB.Types.CustomFilter;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeCustomFilterRepository : SpacetimeBasicRepository<CustomFilter, StdbCustomFilter>, ICustomFilterRepository
    {
        public SpacetimeCustomFilterRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbCustomFilter[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.CustomFilter.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override CustomFilter ToModel(StdbCustomFilter row) =>
            new CustomFilter { Id = row.Id, Type = row.Type, Label = row.Label, Filters = row.Filters };

        protected override int GetRowId(StdbCustomFilter row) => row.Id;

        protected override void InvokeInsertReducer(CustomFilter model) =>
            Conn.Connection.Reducers.InsertCustomFilter(model.Type, model.Label, model.Filters);

        protected override void InvokeUpdateReducer(CustomFilter model) =>
            Conn.Connection.Reducers.UpdateCustomFilter(model.Id, model.Type, model.Label, model.Filters);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteCustomFilter(id);
    }
}
