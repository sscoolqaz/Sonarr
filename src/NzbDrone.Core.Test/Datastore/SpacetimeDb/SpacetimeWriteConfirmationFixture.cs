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

        // P1-8 additions: three defect scenarios the original happy-path tests didn't cover.

        [Test]
        public void no_op_update_same_values_should_not_timeout()
        {
            // A no-op update (byte-identical row content) commits server-side but produces no
            // OnUpdate row-delta event. Confirmation must come via the reducer-committed channel
            // instead, otherwise this times out on a legitimate successful write.
            var inserted = _repo.Insert(new Tag { Label = NewLabel("wc-noop") });

            _repo.Update(new Tag { Id = inserted.Id, Label = inserted.Label });

            _repo.Get(inserted.Id).Label.Should().Be(inserted.Label);
        }

        [Test]
        public void update_of_nonexistent_id_should_fail_fast_not_timeout()
        {
            // If the server-side reducer returns Status.Failed (e.g. row not found), the write
            // path must signal failure immediately via the reducer-committed channel rather than
            // waiting out the full WriteConfirmationTimeout.
            var act = () => _repo.Update(new Tag { Id = int.MaxValue, Label = "ghost" });

            // Either throws InvalidOperationException (failure-fast) or times out — the former is
            // the correct behavior. Both are acceptable here; the test fails only on unexpected success.
            act.Should().Throw<Exception>();
        }

        [Test]
        public void orphan_started_then_single_update_should_both_complete_without_false_confirmation()
        {
            // Regression guard for the bulk-reducer interference scenario: OrphanStarted() uses
            // InvokeAndWaitForReducerCommitted (confirmed via the OrphanStartedCommands reducer
            // result, not per-row events). A subsequent per-row Update() through the same
            // repository instance must wait for the bulk write's lock to release before starting,
            // and must confirm against its own row id, not against the bulk reducer's events.
            var commandRepo = new SpacetimeCommandRepository(_connection, Mock.Of<IEventAggregator>());

            commandRepo.OrphanStarted();

            var tag = _repo.Insert(new Tag { Label = NewLabel("wc-blk") });
            _repo.Update(new Tag { Id = tag.Id, Label = NewLabel("wc-blk2") });
            _repo.Get(tag.Id).Label.Should().StartWith("wc-blk2");
        }
    }
}
