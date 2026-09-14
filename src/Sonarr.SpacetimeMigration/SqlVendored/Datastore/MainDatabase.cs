using System;
using System.Data.Common;

namespace NzbDrone.Core.Datastore
{
    public interface IMainDatabase : IDatabase
    {
    }

    public class MainDatabase : IMainDatabase
    {
        private readonly IDatabase _database;

        public MainDatabase(IDatabase database)
        {
            _database = database;
        }

        public DbConnection OpenConnection()
        {
            return _database.OpenConnection();
        }

        public Version Version => _database.Version;

        public int Migration => _database.Migration;

        public DatabaseType DatabaseType => _database.DatabaseType;

        public void Vacuum()
        {
            _database.Vacuum();
        }
    }
}
