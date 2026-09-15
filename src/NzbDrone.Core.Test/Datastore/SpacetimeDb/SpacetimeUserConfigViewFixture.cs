using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Test.Datastore.SpacetimeDb
{
    /// <summary>
    /// Exercises the View-based redesign of User/Config read access: those two tables are now
    /// private (SpacetimeDB requires a table to be private for a View gating it to make sense -
    /// see the SECURITY comments on the User/Config declarations in
    /// Sonarr.SpacetimeModule/spacetimedb/Batch1.cs for the full account, including the earlier
    /// row-level-security attempt this replaced), readable only through the TrustedUsers/
    /// TrustedConfigs views declared alongside them, which gate on TrustedConnection membership
    /// server-side. SpacetimeUserRepository/SpacetimeConfigRepository already point their Table
    /// property at the view handles instead of the raw table handles - this fixture proves that
    /// swap is actually transparent to normal repository usage (full CRUD still works exactly as
    /// it did against the tables directly), and separately proves the access restriction itself
    /// is real by hitting the HTTP SQL endpoint directly with no authentication.
    ///
    /// Like the other fixtures in this folder, this can't be faked in isolation - it needs a real
    /// subscribed RemoteTableHandle backed by a live SpacetimeDB server. Requires the
    /// Sonarr.SpacetimeModule module published as "sonarr-spacetime-dev" reachable at
    /// http://127.0.0.1:3000 (see docker/spacetimedb-dev/ for the dev podman-compose setup).
    /// </summary>
    [TestFixture]
    [Explicit("Requires a live SpacetimeDB server - see class remarks")]
    public class SpacetimeUserConfigViewFixture
    {
        private ISpacetimeDbConnection _connection;
        private SpacetimeConfigRepository _configRepo;
        private SpacetimeUserRepository _userRepo;

        [SetUp]
        public void Setup()
        {
            _connection = new SpacetimeDbConnection("http://127.0.0.1:3000", "sonarr-spacetime-dev");
            _configRepo = new SpacetimeConfigRepository(_connection, Mock.Of<IEventAggregator>());
            _userRepo = new SpacetimeUserRepository(_connection, Mock.Of<IEventAggregator>());
        }

        [TearDown]
        public void TearDown()
        {
            (_connection as IDisposable)?.Dispose();
        }

        private static string NewKey(string prefix) => $"{prefix}-{Guid.NewGuid():N}".Substring(0, 24);

        [Test]
        public async Task config_crud_should_round_trip_through_the_trusted_configs_view()
        {
            var key = NewKey("view-cfg");

            var inserted = await _configRepo.Insert(new Config { Key = key, Value = "v1" });
            inserted.Id.Should().BeGreaterThan(0);

            (await _configRepo.Get(key)).Value.Should().Be("v1");

            var found = await _configRepo.Find(inserted.Id);
            found.Should().NotBeNull("Find() reads through the view handle's own Id index the same way it would against the raw table");
            found.Value.Should().Be("v1");

            await _configRepo.Update(new Config { Id = inserted.Id, Key = key, Value = "v2" });
            (await _configRepo.Get(key)).Value.Should().Be("v2", "the view is a read-through projection, not a stale snapshot - it must reflect the write immediately");

            await _configRepo.Delete(inserted.Id);
            (await _configRepo.Find(inserted.Id)).Should().BeNull();
        }

        [Test]
        public async Task config_upsert_should_work_through_the_view_backed_read_path()
        {
            // Upsert's own Get(key)-then-Insert-or-Update logic is exactly the kind of thing
            // that would silently break if the view handle behaved even slightly differently
            // from a raw table handle (e.g. stale reads, wrong primary-key semantics).
            var key = NewKey("view-upsert");

            var created = await _configRepo.Upsert(key, "first");
            created.Value.Should().Be("first");

            var updated = await _configRepo.Upsert(key, "second");
            updated.Id.Should().Be(created.Id, "Upsert should find the existing row through the view and update it, not insert a duplicate");
            updated.Value.Should().Be("second");

            await _configRepo.Delete(created.Id);
        }

        [Test]
        public async Task user_crud_should_round_trip_through_the_trusted_users_view()
        {
            var identifier = Guid.NewGuid();
            var username = NewKey("view-user");

            var inserted = await _userRepo.Insert(new User
            {
                Identifier = identifier,
                Username = username,
                Password = "hash",
                Salt = "salt",
                Iterations = 1000
            });
            inserted.Id.Should().BeGreaterThan(0);

            (await _userRepo.FindUser(username)).Should().NotBeNull();
            (await _userRepo.FindUser(identifier)).Should().NotBeNull();

            await _userRepo.Update(new User
            {
                Id = inserted.Id,
                Identifier = identifier,
                Username = username,
                Password = "newhash",
                Salt = "salt",
                Iterations = 2000
            });

            (await _userRepo.FindUser(username)).Password.Should().Be("newhash");

            await _userRepo.Delete(inserted.Id);
            (await _userRepo.Find(inserted.Id)).Should().BeNull();
        }

        [Test]
        public async Task anonymous_http_sql_against_the_private_user_and_config_tables_should_be_denied()
        {
            // The repository-level tests above prove the view swap is functionally transparent
            // for a trusted connection - this test proves the actual security property the whole
            // redesign exists for: an unauthenticated caller hitting the raw table directly
            // (bypassing the app, the SDK, and TrustedConnection entirely) gets nothing.
            using var http = new HttpClient();

            async Task<string> QuerySql(string sql)
            {
                var response = await http.PostAsync(
                    "http://127.0.0.1:3000/v1/database/sonarr-spacetime-dev/sql",
                    new StringContent(sql, Encoding.UTF8, "text/plain"));

                return await response.Content.ReadAsStringAsync();
            }

            var userResult = await QuerySql("SELECT * FROM user");
            var configResult = await QuerySql("SELECT * FROM config");

            userResult.Should().NotContain("\"rows\"", "the user table is private - an anonymous HTTP SQL query should be denied, not return row data");
            configResult.Should().NotContain("\"rows\"", "the config table is private - an anonymous HTTP SQL query should be denied, not return row data");

            // Sanity check that this test would actually catch a real regression: a table that
            // IS meant to be readable (tag) should still come back with a normal rows payload
            // through the exact same anonymous, unauthenticated HTTP path.
            var tagResult = await QuerySql("SELECT * FROM tag");
            tagResult.Should().Contain("\"rows\"", "tag is a genuinely public table - if this assertion fails, the QuerySql helper itself is broken, not the security property under test");
        }
    }
}
