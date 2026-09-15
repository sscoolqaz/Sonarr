using System;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Test.Datastore.SpacetimeDb
{
    /// <summary>
    /// Exercises SpacetimeBasicRepository's always-on row-event listeners (OnForeignRowInserted/
    /// OnForeignRowUpdated/OnForeignRowDeleted, registered once per instance by
    /// EnsureRowEventListenersRegisteredAsync) - the "event ownership" fix: a write this
    /// repository's own connection did NOT cause (here, a second independent connection to the
    /// same database) still needs to publish a ModelEvent&lt;TModel&gt; via IEventAggregator, not
    /// just this connection's own confirmed writes.
    ///
    /// Uses SpacetimeRootFolderRepository specifically because RootFolder is one of the few
    /// entities with PublishModelEvents => true (SpacetimeTagRepository, the usual stand-in
    /// elsewhere in this test project, defaults PublishModelEvents to false and so would never
    /// publish anything for either the self- or foreign-write path, making it the wrong choice
    /// here - PublishModelEvent's own PublishModelEvents-gate applies identically to both the
    /// explicit self-write call and the new always-on foreign-write listeners).
    ///
    /// Like SpacetimeWriteConfirmationFixture, this can't be faked in isolation - it needs a real
    /// Table.OnInsert/OnUpdate/OnDelete event to actually fire, which only happens against a live
    /// subscribed RemoteTableHandle. Requires a SpacetimeDB server with the Sonarr.SpacetimeModule
    /// module published as "sonarr-spacetime-dev" reachable at http://127.0.0.1:3000 (see
    /// docker/spacetimedb-dev/ for the dev podman-compose setup). Uses two independent
    /// SpacetimeDbConnection instances (two real connections to the same database) to produce a
    /// genuine "someone else wrote this row" scenario, rather than simulating one.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a live SpacetimeDB server - see class remarks")]
    public class SpacetimeEventOwnershipFixture
    {
        private ISpacetimeDbConnection _observerConnection;
        private ISpacetimeDbConnection _writerConnection;
        private Mock<IEventAggregator> _observerEventAggregator;
        private SpacetimeRootFolderRepository _observerRepo;
        private SpacetimeRootFolderRepository _writerRepo;

        [SetUp]
        public void Setup()
        {
            _observerConnection = new SpacetimeDbConnection("http://127.0.0.1:3000", "sonarr-spacetime-dev");
            _writerConnection = new SpacetimeDbConnection("http://127.0.0.1:3000", "sonarr-spacetime-dev");

            _observerEventAggregator = new Mock<IEventAggregator>();
            _observerRepo = new SpacetimeRootFolderRepository(_observerConnection, _observerEventAggregator.Object);
            _writerRepo = new SpacetimeRootFolderRepository(_writerConnection, Mock.Of<IEventAggregator>());
        }

        [TearDown]
        public void TearDown()
        {
            (_observerConnection as IDisposable)?.Dispose();
            (_writerConnection as IDisposable)?.Dispose();
        }

        private static string NewPath(string prefix) => $"/{prefix}-{Guid.NewGuid():N}".Substring(0, 24);

        // Polls rather than a fixed delay: the observer connection's actor thread only picks up
        // the writer's row event on its own FrameTick() cadence (every 5ms - see
        // SpacetimeDbConnection.RunLoop), and CI/dev-container scheduling jitter makes a single
        // fixed sleep an unreliable choice between "flaky" and "needlessly slow every run".
        private static async Task WaitUntil(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;

            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                {
                    return;
                }

                await Task.Delay(25);
            }
        }

        [Test]
        public async Task insert_from_a_different_connection_should_publish_a_model_event_on_the_observer()
        {
            // The observer must have touched this repository at least once (any read or write)
            // before the writer's row lands, so EnsureRowEventListenersRegisteredAsync has
            // already registered the always-on listeners - exactly like a real repository
            // singleton would have been touched during normal app startup/use long before some
            // external write happens.
            await _observerRepo.All();

            var path = NewPath("evt-ins");
            var inserted = await _writerRepo.Insert(new RootFolder { Path = path });

            await WaitUntil(
                () => _observerEventAggregator.Invocations.Count > 0,
                TimeSpan.FromSeconds(5));

            _observerEventAggregator.Verify(
                e => e.PublishEvent(It.Is<ModelEvent<RootFolder>>(evt =>
                    evt.ModelId == inserted.Id && evt.Action == ModelAction.Created)),
                Times.AtLeastOnce,
                "the observer connection did not cause this insert, but its always-on OnForeignRowInserted listener should still have published a ModelEvent for it");
        }

        [Test]
        public async Task update_from_a_different_connection_should_publish_a_model_event_on_the_observer()
        {
            var original = await _observerRepo.Insert(new RootFolder { Path = NewPath("evt-upd") });

            // The observer's own Insert() above already publishes its own ModelEvent<RootFolder>
            // (Created) via the ordinary explicit-confirmation path - reset invocation history so
            // this test's assertion is unambiguously about the externally-caused Update below,
            // not a leftover from the observer's own insert.
            _observerEventAggregator.Invocations.Clear();

            var updatedPath = NewPath("evt-upd2");
            await _writerRepo.Update(new RootFolder { Id = original.Id, Path = updatedPath });

            await WaitUntil(
                () => _observerEventAggregator.Invocations.Count > 0,
                TimeSpan.FromSeconds(5));

            _observerEventAggregator.Verify(
                e => e.PublishEvent(It.Is<ModelEvent<RootFolder>>(evt =>
                    evt.ModelId == original.Id && evt.Action == ModelAction.Updated)),
                Times.AtLeastOnce,
                "the observer connection did not cause this update, but its always-on OnForeignRowUpdated listener should still have published a ModelEvent for it");
        }

        [Test]
        public async Task delete_from_a_different_connection_should_publish_a_model_event_on_the_observer()
        {
            var original = await _observerRepo.Insert(new RootFolder { Path = NewPath("evt-del") });
            _observerEventAggregator.Invocations.Clear();

            await _writerRepo.Delete(original.Id);

            await WaitUntil(
                () => _observerEventAggregator.Invocations.Count > 0,
                TimeSpan.FromSeconds(5));

            _observerEventAggregator.Verify(
                e => e.PublishEvent(It.Is<ModelEvent<RootFolder>>(evt =>
                    evt.ModelId == original.Id && evt.Action == ModelAction.Deleted)),
                Times.AtLeastOnce,
                "the observer connection did not cause this delete, but its always-on OnForeignRowDeleted listener should still have published a ModelEvent for it");
        }

        [Test]
        public async Task a_connections_own_confirmed_write_should_not_be_published_twice()
        {
            // Regression guard for the double-publish gap the fix specifically avoids: the
            // observer's OWN write is already published once via the explicit PublishModelEvent
            // call after Insert() confirms - the always-on OnForeignRowInserted listener must
            // recognize this as IsOwnConnectionEvent and skip it, not publish a second time.
            await _observerRepo.All();
            _observerEventAggregator.Invocations.Clear();

            var inserted = await _observerRepo.Insert(new RootFolder { Path = NewPath("evt-self") });

            // Give the actor a moment to also process/skip the row event before asserting a
            // negative (there's nothing to poll FOR here, only the absence of a second publish).
            await Task.Delay(500);

            _observerEventAggregator.Verify(
                e => e.PublishEvent(It.Is<ModelEvent<RootFolder>>(evt =>
                    evt.ModelId == inserted.Id && evt.Action == ModelAction.Created)),
                Times.Once,
                "a self-caused write must be published exactly once - by the explicit PublishModelEvent call after confirmation, not a second time by the always-on foreign-write listener");
        }
    }
}
