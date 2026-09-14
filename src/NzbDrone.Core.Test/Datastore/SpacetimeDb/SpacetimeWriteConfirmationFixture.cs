using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;

namespace NzbDrone.Core.Test.Datastore.SpacetimeDb
{
    /// <summary>
    /// Exercises SpacetimeBasicRepository's event-based write-confirmation mechanism: Insert,
    /// Update, SetFields and Delete all subscribe to the table's OnInsert/OnUpdate/OnDelete
    /// callback, correlate it to this connection's own reducer call via
    /// ReducerEvent.CallerIdentity/CallerConnectionId, and only return once that confirmation
    /// arrives. This can't be faked in isolation - it needs a live subscription and a real
    /// RemoteTableHandle&lt;EventContext, TStdbRow&gt;, not a hand-rolled RemoteQuery double -
    /// so unlike the mechanism's predecessor (a polling-based heuristic, since replaced), this
    /// is only verifiable against a live server. Uses SpacetimeTagRepository as a simple, proven
    /// stand-in - the mechanism under test lives entirely in the shared base class, not in Tag's
    /// own mapping code.
    ///
    /// Requires a SpacetimeDB server with the Sonarr.SpacetimeModule module published as
    /// "sonarr-spacetime-dev" reachable at http://127.0.0.1:3000 (see
    /// .dev-scratch/spacetimedb-standalone for the dev podman-compose setup).
    /// </summary>
    [TestFixture]
    [Explicit("Requires a live SpacetimeDB server - see class remarks")]
    public class SpacetimeWriteConfirmationFixture
    {
        private ISpacetimeDbConnection _connection;
        private SpacetimeTagRepository _repo;

        [SetUp]
        public void Setup()
        {
            _connection = new SpacetimeDbConnection("http://127.0.0.1:3000", "sonarr-spacetime-dev");
            _repo = new SpacetimeTagRepository(_connection, Mock.Of<IEventAggregator>());
        }

        [TearDown]
        public void TearDown()
        {
            (_connection as IDisposable)?.Dispose();
        }

        private static string NewLabel(string prefix) => $"{prefix}-{Guid.NewGuid():N}".Substring(0, 20);

        [Test]
        public void insert_should_resolve_to_this_connections_own_row_id()
        {
            var label = NewLabel("wc-ins");

            var inserted = _repo.Insert(new Tag { Label = label });

            inserted.Id.Should().BeGreaterThan(0);
            _repo.Get(inserted.Id).Label.Should().Be(label);
        }

        [Test]
        public void rapid_sequential_inserts_should_each_resolve_to_their_own_row()
        {
            for (var i = 0; i < 10; i++)
            {
                var label = NewLabel($"wc-seq{i}");

                var inserted = _repo.Insert(new Tag { Label = label });

                _repo.Get(inserted.Id).Label.Should().Be(label);
            }
        }

        [Test]
        public void update_should_wait_until_the_new_value_is_confirmed()
        {
            var original = _repo.Insert(new Tag { Label = NewLabel("wc-upd") });
            var updatedLabel = NewLabel("wc-upd2");

            _repo.Update(new Tag { Id = original.Id, Label = updatedLabel });

            _repo.Get(original.Id).Label.Should().Be(updatedLabel);
        }

        [Test]
        public void set_fields_should_wait_until_the_field_is_confirmed()
        {
            var original = _repo.Insert(new Tag { Label = NewLabel("wc-sf") });
            var updatedLabel = NewLabel("wc-sf2");

            _repo.SetFields(new Tag { Id = original.Id, Label = updatedLabel }, t => t.Label);

            _repo.Get(original.Id).Label.Should().Be(updatedLabel);
        }

        [Test]
        public void delete_should_wait_until_the_row_is_confirmed_removed()
        {
            var inserted = _repo.Insert(new Tag { Label = NewLabel("wc-del") });

            _repo.Delete(inserted.Id);

            _repo.Find(inserted.Id).Should().BeNull();
        }
    }
}
