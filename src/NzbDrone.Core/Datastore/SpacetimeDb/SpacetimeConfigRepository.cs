using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using StdbConfig = SpacetimeDB.Types.Config;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeConfigRepository : SpacetimeBasicRepository<Config, StdbConfig>, IConfigRepository
    {
        public SpacetimeConfigRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbConfig[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.Config.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override Config ToModel(StdbConfig row) => new Config { Id = row.Id, Key = row.Key, Value = row.Value };

        protected override int GetRowId(StdbConfig row) => row.Id;

        protected override void InvokeInsertReducer(Config model) => Conn.Connection.Reducers.InsertConfig(model.Key ?? string.Empty, model.Value ?? string.Empty);

        public override void MigrateInsert(Config model) => Conn.Connection.Reducers.MigrateInsertConfig(model.Id, model.Key ?? string.Empty, model.Value ?? string.Empty);

        protected override void InvokeUpdateReducer(Config model) => Conn.Connection.Reducers.UpdateConfig(model.Id, model.Key ?? string.Empty, model.Value ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteConfig(id);

        public Config Get(string key) =>
            RemoteQuery($"WHERE Key = '{EscapeSqlString(key)}'").Select(ToModel).SingleOrDefault();

        public Config Upsert(string key, string value)
        {
            var dbValue = Get(key);

            if (dbValue == null)
            {
                return Insert(new Config { Key = key, Value = value });
            }

            dbValue.Value = value;

            return Update(dbValue);
        }
    }
}
