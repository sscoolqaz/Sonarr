using System;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using StdbTag = SpacetimeDB.Types.Tag;

namespace NzbDrone.Core.Test.Datastore.SpacetimeDb
{
    /// <summary>
    /// Unit-tests SpacetimeBasicRepository's async plumbing in isolation, with a mocked
    /// ISpacetimeDbConnection instead of a live SpacetimeDB server. This deliberately does NOT
    /// duplicate SpacetimeWriteConfirmationFixture (which needs a real subscribed
    /// RemoteTableHandle to correlate Insert's OnInsert row event - still the sole source of a
    /// new row's server-assigned id - and so can only run against a live server) - it covers the
    /// parts of the base class that don't touch Table at all: the per-instance
    /// write-serialization lock, the confirmation-wait's timeout-to-failure translation, the
    /// RegisterPendingOperation fail-fast path, and PublishModelEvent's PublishModelEvents/
    /// forcePublish gating.
    ///
    /// InvokeAndWaitForReducerCommitted (the mechanism behind SpacetimeCommandRepository's bulk
    /// reducers, e.g. OrphanStarted) is used as the exercise surface for the lock/timeout/failure
    /// behavior instead of Insert/Update/Delete directly. Update and Delete now share this exact
    /// mechanism themselves (both confirm exclusively via their own reducer-committed result, no
    /// row event at all), so exercising it here already covers their shared logic too; Insert
    /// still additionally needs a live Table.OnInsert row event for the new row's id, which is a
    /// concrete generated RemoteTableHandle&lt;EventContext, TStdbRow&gt; with no live-server-free
    /// way to raise that event, so a fake Table would test the fake rather than the real class.
    /// Every write path (Insert/Update/SetFields/Delete/InvokeAndWaitForMigrateInsert/
    /// InvokeAndWaitForReducerCommitted) shares the exact same
    /// _writeLock/WaitForConfirmation/RegisterPendingOperation code, so exercising it through the
    /// one Table-free path still covers the real shared logic.
    /// </summary>
    [TestFixture]
    public class SpacetimeBasicRepositoryPlumbingFixture
    {
        private Mock<ISpacetimeDbConnection> _connection;
        private Mock<IEventAggregator> _eventAggregator;

        [SetUp]
        public void Setup()
        {
            _connection = new Mock<ISpacetimeDbConnection>();
            _eventAggregator = new Mock<IEventAggregator>();

            // Every write path funnels registration/unregistration work through
            // Conn.RunOnActorAsync - for these tests there's no real actor thread, so just run
            // the delegate inline and hand back a completed Task, the same way
            // SpacetimeDbConnection.RunOnActorAsync itself behaves when already called from its
            // own actor thread (see that class's remarks).
            _connection.Setup(c => c.RunOnActorAsync(It.IsAny<Func<IDisposable>>()))
                .Returns((Func<IDisposable> work) => Task.FromResult(work()));

            _connection.Setup(c => c.RunOnActorAsync(It.IsAny<Action>()))
                .Returns((Action work) =>
                {
                    work();
                    return Task.CompletedTask;
                });
        }

        private TestableSpacetimeRepository CreateRepo(bool publishModelEvents = false, TimeSpan? writeConfirmationTimeout = null)
        {
            return new TestableSpacetimeRepository(
                _connection.Object,
                _eventAggregator.Object,
                publishModelEvents,
                writeConfirmationTimeout ?? TimeSpan.FromMilliseconds(200));
        }

        // ------------------------------------------------------------------
        // Write-lock serialization (_writeLock is a SemaphoreSlim(1,1))
        // ------------------------------------------------------------------

        [Test]
        public async Task second_write_should_not_invoke_its_reducer_until_first_writes_confirmation_releases_the_lock()
        {
            var repo = CreateRepo();

            Action onFirstCommitted = null;
            var firstReducerInvoked = false;
            var firstOp = repo.RunBulkOp(
                invokeReducer: () => firstReducerInvoked = true,
                subscribeCommitted: (onCommitted, onFailed) =>
                {
                    onFirstCommitted = onCommitted;
                    return Mock.Of<IDisposable>();
                });

            firstReducerInvoked.Should().BeTrue("the first write should invoke its reducer immediately - nothing is blocking it yet");
            firstOp.IsCompleted.Should().BeFalse("the first write is still waiting for its confirmation callback");

            var secondReducerInvoked = false;
            var secondOp = repo.RunBulkOp(
                invokeReducer: () => secondReducerInvoked = true,
                subscribeCommitted: (onCommitted, onFailed) =>
                {
                    onCommitted();
                    return Mock.Of<IDisposable>();
                });

            await Task.Delay(50);
            secondReducerInvoked.Should().BeFalse("the second write must wait for _writeLock until the first write's confirmation releases it");

            onFirstCommitted();
            await firstOp;

            await secondOp;
            secondReducerInvoked.Should().BeTrue("once the first write releases the lock, the second write should proceed and invoke its own reducer");
        }

        // ------------------------------------------------------------------
        // WaitForConfirmation timeout -> failure translation
        // ------------------------------------------------------------------

        [Test]
        public void a_confirmation_that_never_arrives_should_throw_after_the_configured_timeout_not_hang()
        {
            var repo = CreateRepo(writeConfirmationTimeout: TimeSpan.FromMilliseconds(100));

            Func<Task> act = () => repo.RunBulkOp(
                invokeReducer: () => { },
                subscribeCommitted: (onCommitted, onFailed) => Mock.Of<IDisposable>());

            act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*was not confirmed within*");
        }

        [Test]
        public async Task the_write_lock_should_be_released_even_after_a_confirmation_timeout()
        {
            // Regression guard: WaitForConfirmation swallows TimeoutException into a `false`
            // result inside a try/finally that releases _writeLock - if that release were ever
            // skipped, every subsequent write through this repository instance would hang
            // forever waiting on the semaphore instead of surfacing its own error.
            var repo = CreateRepo(writeConfirmationTimeout: TimeSpan.FromMilliseconds(100));

            Func<Task> firstAct = () => repo.RunBulkOp(
                invokeReducer: () => { },
                subscribeCommitted: (onCommitted, onFailed) => Mock.Of<IDisposable>());

            await firstAct.Should().ThrowAsync<InvalidOperationException>();

            var secondReducerInvoked = false;
            await repo.RunBulkOp(
                invokeReducer: () => secondReducerInvoked = true,
                subscribeCommitted: (onCommitted, onFailed) =>
                {
                    onCommitted();
                    return Mock.Of<IDisposable>();
                });

            secondReducerInvoked.Should().BeTrue("the lock must be released on the timeout path, not just the success path");
        }

        // ------------------------------------------------------------------
        // Reducer-committed failure channel (Status.Failed/OutOfEnergy)
        // ------------------------------------------------------------------

        [Test]
        public void a_reducer_reported_as_failed_should_throw_immediately_rather_than_waiting_out_the_timeout()
        {
            var repo = CreateRepo(writeConfirmationTimeout: TimeSpan.FromSeconds(5));

            Func<Task> act = () => repo.RunBulkOp(
                invokeReducer: () => { },
                subscribeCommitted: (onCommitted, onFailed) =>
                {
                    onFailed(new InvalidOperationException("Reducer failed with status Failed"));
                    return Mock.Of<IDisposable>();
                });

            // The 5s timeout above is intentionally much longer than this test should take -
            // ThrowAsync itself has no built-in deadline, so this test's own failure mode (a hang)
            // would be a real regression indicator, not just a slow pass.
            act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*outcome is unknown*");
        }

        // ------------------------------------------------------------------
        // RegisterPendingOperation: connection lost while a write is pending
        // ------------------------------------------------------------------

        [Test]
        public void a_connection_already_lost_when_a_write_starts_should_fail_fast_not_wait_out_the_timeout()
        {
            var repo = CreateRepo(writeConfirmationTimeout: TimeSpan.FromSeconds(5));

            _connection.Setup(c => c.RegisterPendingOperation(It.IsAny<Action<Exception>>()))
                .Returns((Action<Exception> onLost) =>
                {
                    // Mirrors SpacetimeDbConnection.RegisterPendingOperation's own already-
                    // disconnected branch: invoke the callback synchronously instead of waiting
                    // for a future disconnect event.
                    onLost(new InvalidOperationException("SpacetimeDB connection is disconnected."));
                    return Mock.Of<IDisposable>();
                });

            Func<Task> act = () => repo.RunBulkOp(
                invokeReducer: () => { },
                subscribeCommitted: (onCommitted, onFailed) => Mock.Of<IDisposable>());

            act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*outcome is unknown*");
        }

        // ------------------------------------------------------------------
        // PublishModelEvent gating (PublishModelEvents / forcePublish)
        // ------------------------------------------------------------------

        [Test]
        public void model_updated_should_publish_when_the_entity_opts_into_publishing_events()
        {
            var repo = CreateRepo(publishModelEvents: true);
            var model = new Tag { Id = 7, Label = "opted-in" };

            repo.TriggerModelUpdated(model);

            _eventAggregator.Verify(
                e => e.PublishEvent(It.Is<ModelEvent<Tag>>(evt => evt.ModelId == 7 && evt.Action == ModelAction.Updated)),
                Times.Once);
        }

        [Test]
        public void model_updated_should_not_publish_when_the_entity_does_not_opt_in_and_forcePublish_is_not_set()
        {
            var repo = CreateRepo(publishModelEvents: false);
            var model = new Tag { Id = 8, Label = "opted-out" };

            repo.TriggerModelUpdated(model);

            _eventAggregator.Verify(e => e.PublishEvent(It.IsAny<ModelEvent<Tag>>()), Times.Never);
        }

        [Test]
        public void model_updated_should_publish_when_forcePublish_is_set_even_if_the_entity_does_not_opt_in()
        {
            // Mirrors the real EpisodeRepository's use of ModelUpdated(model, forcePublish: true)
            // for SetMonitoredFlat/SetFileId/ClearFileId - a specific call forcing an event even
            // when PublishModelEvents is false for the entity as a whole.
            var repo = CreateRepo(publishModelEvents: false);
            var model = new Tag { Id = 9, Label = "forced" };

            repo.TriggerModelUpdated(model, forcePublish: true);

            _eventAggregator.Verify(
                e => e.PublishEvent(It.Is<ModelEvent<Tag>>(evt => evt.ModelId == 9 && evt.Action == ModelAction.Updated)),
                Times.Once);
        }

        /// <summary>
        /// Minimal concrete leaf for exercising SpacetimeBasicRepository's shared plumbing
        /// without a live SpacetimeDB server. Table/FindRowById/ToModel/GetRowId/the Invoke*Reducer
        /// hooks/SubscribeOwnUpdateCommitted are all intentionally unimplemented (throw) - every
        /// test above only calls the Table-free members (InvokeAndWaitForReducerCommitted,
        /// ModelUpdated) via the public wrappers below, so those hooks are never reached. Reuses
        /// Tag/StdbTag as generic type arguments purely because they already satisfy TModel/TStdbRow's
        /// constraints - no Tag-specific behavior is under test here.
        /// </summary>
        private sealed class TestableSpacetimeRepository : SpacetimeBasicRepository<Tag, StdbTag>
        {
            private readonly bool _publishModelEvents;
            private readonly TimeSpan _writeConfirmationTimeout;

            public TestableSpacetimeRepository(
                ISpacetimeDbConnection connection,
                IEventAggregator eventAggregator,
                bool publishModelEvents,
                TimeSpan writeConfirmationTimeout)
                : base(connection, eventAggregator)
            {
                _publishModelEvents = publishModelEvents;
                _writeConfirmationTimeout = writeConfirmationTimeout;
            }

            protected override bool PublishModelEvents => _publishModelEvents;

            protected override TimeSpan WriteConfirmationTimeout => _writeConfirmationTimeout;

            protected override RemoteTableHandle<EventContext, StdbTag> Table =>
                throw new NotSupportedException("Table is not exercised by the plumbing tests - see fixture remarks.");

            protected override StdbTag FindRowById(int id) => throw new NotSupportedException();

            protected override Tag ToModel(StdbTag row) => throw new NotSupportedException();

            protected override int GetRowId(StdbTag row) => throw new NotSupportedException();

            protected override void InvokeInsertReducer(Tag model) => throw new NotSupportedException();

            protected override void InvokeUpdateReducer(Tag model) => throw new NotSupportedException();

            protected override void InvokeDeleteReducer(int id) => throw new NotSupportedException();

            protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed) =>
                throw new NotSupportedException();

            protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed) =>
                throw new NotSupportedException();

            protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed) =>
                throw new NotSupportedException();

            public Task RunBulkOp(Action invokeReducer, Func<Action, Action<Exception>, IDisposable> subscribeCommitted) =>
                InvokeAndWaitForReducerCommitted(invokeReducer, subscribeCommitted);

            public void TriggerModelUpdated(Tag model, bool forcePublish = false) => ModelUpdated(model, forcePublish);
        }
    }
}
