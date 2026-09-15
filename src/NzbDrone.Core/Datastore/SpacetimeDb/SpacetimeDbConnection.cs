using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NLog;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// One shared SpacetimeDB connection for the whole app, injected into every
    /// SpacetimeBasicRepository&lt;,&gt; instance instead of each repository owning its own
    /// connection (which is what the Phase 1 spike did - fine for one entity, wasteful at the
    /// ~40-entity scale Phase 4 ports).
    ///
    /// This is a single-threaded actor for every interaction with the generated SDK's Db/Table
    /// state: FrameTick(), all table row-event and reducer-result callback registration, and all
    /// cache reads happen exclusively on the dedicated actor thread (see RunOnActor below). The
    /// SDK's own guidance is explicit that FrameTick() mutates the client cache and that
    /// concurrent access to Db from another thread is unsafe while it runs - this was confirmed
    /// as a real bug in an earlier version of this file (repositories called `Table.OnInsert +=`
    /// directly from the calling thread, racing this connection's FrameTick pump thread; the
    /// installed SpacetimeDB.ClientSDK.dll's EventListeners&lt;T&gt; is a plain unsynchronized
    /// List/Dictionary with no protection against this). Routing every touch of Connection.Db
    /// through RunOnActor removes that race by construction: registration and invocation are the
    /// same thread, never concurrent, instead of being synchronized after the fact.
    ///
    /// Subscribes to every table up front, blocking the constructor until the initial sync
    /// completes - this is the actual reason to reach for SpacetimeDB's subscription model
    /// instead of treating it as a conventional request/response database: every
    /// SpacetimeBasicRepository's Insert/Update/Delete correlates its own writes by matching a
    /// correlated event's CallerIdentity/CallerConnectionId against this connection's own, which
    /// for Insert's row event only ever fires for tables this connection is subscribed to (Update/
    /// Delete's own reducer-committed confirmation doesn't need a table subscription at all - see
    /// SpacetimeBasicRepository's remarks). SubscribeToAllTables() is used here for simplicity at
    /// this app's current ~40-entity scale, NOT because write-correlation strictly requires it:
    /// narrower, explicit per-table subscriptions would deliver the exact same row events and
    /// would work equally well for Insert's correlation. This is a "measure and revisit if row
    /// counts/startup latency become an issue" choice, not a hard requirement - it trades a
    /// slower app startup (one full sync of the whole database) for not having to keep a
    /// per-table subscription list in sync with the entity list as it grows.
    ///
    /// A spot-check of this session's dev database found current row counts trivial (Episode
    /// ~82, EpisodeFile ~2, History ~3, Command ~3, Series ~3, Tag ~18) - that database is a
    /// fresh test install, not representative of real usage, and this measurement does NOT
    /// settle the question for a real user's library (which could plausibly have thousands of
    /// Episode/History rows). "Measure and revisit" therefore remains a genuinely open question
    /// this session's spot-check did not resolve, not something confirmed fine at scale.
    /// </summary>
    public interface ISpacetimeDbConnection
    {
        SpacetimeDB.Types.DbConnection Connection { get; }

        /// <summary>
        /// Runs work on the dedicated actor thread and blocks the caller until it completes,
        /// returning its result. The only safe way to read Connection.Db or touch a table/reducer
        /// handle's callback registration - never do either directly from a caller thread. Calling
        /// this from the actor thread itself (e.g. from inside a callback that needs to register
        /// another one) runs inline rather than deadlocking against itself.
        /// </summary>
        T RunOnActor<T>(Func<T> work);

        /// <summary>
        /// Void-returning variant of RunOnActor - still blocks the caller until the work has run
        /// on the actor thread, just without a result to hand back. Used for callback
        /// registration/unregistration (e.g. Table.OnInsert +=/-=), where the point is to
        /// guarantee the add/remove has actually happened - and is ordered against FrameTick() -
        /// before the caller proceeds, not to fire it off and hope.
        /// </summary>
        void RunOnActor(Action work);

        /// <summary>
        /// Async, non-blocking variant of RunOnActor - queues work onto the actor thread and
        /// returns a Task that completes with its result once the actor processes it, instead of
        /// blocking the calling thread until that happens. This is what SpacetimeBasicRepository's
        /// public (IBasicRepository) methods use: the calling thread (an ASP.NET request thread,
        /// ultimately) is freed to do other work while the actor thread pump handles the queued
        /// item on its own schedule, rather than parking a thread-pool thread for the duration.
        /// Called from the actor thread itself runs inline, returning an already-completed Task,
        /// same reasoning as RunOnActor's own inline case.
        /// </summary>
        Task<T> RunOnActorAsync<T>(Func<T> work);

        /// <summary>
        /// Void-returning variant of RunOnActorAsync.
        /// </summary>
        Task RunOnActorAsync(Action work);

        /// <summary>
        /// Registers a callback to run if the connection is lost while some operation is pending
        /// (e.g. a write waiting for row/reducer confirmation) - callers register one of these for
        /// as long as they're waiting, and unregister (via the returned IDisposable) once they get
        /// a real result, so a disconnect can fail every still-pending wait instead of leaving them
        /// blocked until their own timeout.
        /// </summary>
        IDisposable RegisterPendingOperation(Action<Exception> onConnectionLost);
    }

    public class SpacetimeDbConnection : ISpacetimeDbConnection, IDisposable
    {
        // Safety-net bound on the actor's work queue, not a throttle - at Sonarr's realistic
        // scale this should never come close to being hit; it exists purely so a caller pileup
        // (something enqueuing work faster than the single actor thread's FrameTick()+drain loop
        // can process it) applies backpressure instead of growing the queue without limit.
        private const int WorkQueueCapacity = 4096;

        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan SubscribeTimeout = TimeSpan.FromSeconds(60);

        private readonly Thread _actorThread;

        // A bounded System.Threading.Channels.Channel<Action>, not a bounded BlockingCollection -
        // chosen specifically because RunOnActorAsync (used by every SpacetimeBasicRepository
        // public method) is documented to not block its caller's thread at all; a bounded
        // BlockingCollection.Add() blocking synchronously when full would undercut that contract
        // the moment the queue is ever actually full. Channel<T>.Writer.WriteAsync gives
        // RunOnActorAsync a genuinely non-blocking (awaitable) enqueue under backpressure, while
        // RunOnActor - the still-used synchronous variant (e.g. constructor-time setup) - awaits
        // the same WriteAsync via .GetAwaiter().GetResult(): blocking the calling thread when the
        // queue is full, same as the bounded BlockingCollection.Add() this replaced would have.
        // That's an intentional, judgment-call choice for RunOnActor specifically: it already
        // blocks its caller until the queued work completes, so blocking it a little longer for
        // queue space is consistent with its existing contract, not a new one - a TryAdd-with-
        // timeout-and-throw alternative would surface a new "actor overloaded" failure mode that
        // nothing in this codebase currently expects or handles, for a capacity that should never
        // realistically be exhausted at this app's scale. FullMode.Wait (rather than DropOldest/
        // DropWrite) is deliberate too: silently dropping a queued repository operation would be
        // silent data loss, not a load-shedding tradeoff worth making here.
        private readonly Channel<Action> _workQueue = Channel.CreateBounded<Action>(
            new BoundedChannelOptions(WorkQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        private readonly CancellationTokenSource _pumpCts = new CancellationTokenSource();
        private readonly ConcurrentDictionary<object, Action<Exception>> _pendingOperations = new ConcurrentDictionary<object, Action<Exception>>();

        private volatile bool _disconnected;
        private volatile Exception _disconnectException;

        public SpacetimeDB.Types.DbConnection Connection { get; }

        public SpacetimeDbConnection(string host, string database, IOidcTokenProvider tokenProvider = null)
        {
            var connectedTcs = new TaskCompletionSource<bool>();

            var builder = SpacetimeDB.Types.DbConnection.Builder()
                .WithUri(host)
                .WithDatabaseName(database)
                .OnConnect((c, identity, token) => connectedTcs.TrySetResult(true))
                .OnConnectError(err => connectedTcs.TrySetException(err))
                .OnDisconnect((c, err) => HandleDisconnect(err));

            var oidcToken = tokenProvider?.GetToken();
            if (oidcToken != null)
            {
                builder = builder.WithToken(oidcToken);
            }

            Connection = builder.Build();

            _actorThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "SpacetimeDB-Actor"
            };
            _actorThread.Start();

            try
            {
                if (!connectedTcs.Task.Wait(ConnectTimeout))
                {
                    throw new TimeoutException($"Timed out connecting to SpacetimeDB at {host} within {ConnectTimeout}");
                }

                var subscribedTcs = new TaskCompletionSource<bool>();

                // Registered via RunOnActor, not directly from this (the constructing) thread -
                // keeps every touch of Connection on the actor thread, not just the ones that
                // obviously read Db, since this is otherwise the one remaining callback
                // registration that bypassed it.
                RunOnActor(() => Connection.SubscriptionBuilder()
                    .OnApplied(ctx => subscribedTcs.TrySetResult(true))
                    .OnError((ctx, ex) => subscribedTcs.TrySetException(ex))
                    .SubscribeToAllTables());

                if (!subscribedTcs.Task.Wait(SubscribeTimeout))
                {
                    throw new TimeoutException($"Timed out waiting for the initial subscription to apply within {SubscribeTimeout}");
                }
            }
            catch
            {
                _pumpCts.Cancel();
                _workQueue.Writer.Complete();
                _actorThread.Join();
                Connection.Disconnect();
                throw;
            }
        }

        public T RunOnActor<T>(Func<T> work)
        {
            if (Thread.CurrentThread == _actorThread)
            {
                return work();
            }

            var tcs = new TaskCompletionSource<T>();

            // Blocks the calling thread if the queue is full (see _workQueue's own remarks for
            // why that's an accepted tradeoff here) - .GetAwaiter().GetResult() rather than
            // .Wait() purely to avoid wrapping any exception in an AggregateException, matching
            // how the unbounded BlockingCollection.Add() this replaced never threw either.
            _workQueue.Writer.WriteAsync(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }).AsTask().GetAwaiter().GetResult();

            return tcs.Task.GetAwaiter().GetResult();
        }

        public void RunOnActor(Action work) => RunOnActor<object>(() =>
        {
            work();
            return null;
        });

        public async Task<T> RunOnActorAsync<T>(Func<T> work)
        {
            if (Thread.CurrentThread == _actorThread)
            {
                return work();
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Genuinely non-blocking even under backpressure: awaiting WriteAsync only yields
            // the calling thread back to its pool instead of blocking it when the queue is full,
            // preserving RunOnActorAsync's documented contract that the caller's own thread is
            // never parked for the duration (see the interface doc comment on this method).
            await _workQueue.Writer.WriteAsync(() =>
            {
                try
                {
                    tcs.SetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return await tcs.Task;
        }

        public Task RunOnActorAsync(Action work) => RunOnActorAsync<object>(() =>
        {
            work();
            return null;
        });

        public IDisposable RegisterPendingOperation(Action<Exception> onConnectionLost)
        {
            // If the connection is already gone when a new write starts, fail immediately
            // rather than waiting out the full WriteConfirmationTimeout for an event that
            // will never arrive.
            if (_disconnected)
            {
                var ex = _disconnectException ?? new InvalidOperationException("SpacetimeDB connection is disconnected.");
                onConnectionLost(ex);
                return new Unregisterer(() => { });
            }

            var key = new object();
            _pendingOperations[key] = onConnectionLost;

            return new Unregisterer(() => _pendingOperations.TryRemove(key, out _));
        }

        private void HandleDisconnect(Exception error)
        {
            var ex = error ?? new InvalidOperationException("The SpacetimeDB connection was disconnected while an operation was pending.");

            _disconnectException = ex;
            _disconnected = true;

            // Log at error level: there is no automatic reconnection — the app must be restarted
            // to restore the SpacetimeDB connection.
            Logger.Error(ex, "SpacetimeDB connection lost — all pending writes have been failed. Restart the application to reconnect.");

            foreach (var pending in _pendingOperations.Values.ToArray())
            {
                pending(ex);
            }
        }

        private void RunLoop()
        {
            // Always drains whatever is in the queue, even on the iteration where cancellation is
            // first observed - a naive `while (!cancelled) { drain; FrameTick(); }` can accept an
            // item into _workQueue (via RunOnActor, right up until Dispose completes the writer)
            // and then never run it: if Cancel() lands between that item being queued and the
            // next drain, the outer loop condition is already false and would exit without ever
            // taking it, leaving that RunOnActor caller blocked on its TaskCompletionSource
            // forever. Draining unconditionally before checking cancellation, and once more after
            // the loop exits, means anything successfully queued is guaranteed to run.
            //
            // Reader.TryRead is the Channel<T> equivalent of the previous BlockingCollection's
            // TryTake(out action, 0) - a non-blocking, immediate check, not a wait.
            while (true)
            {
                while (_workQueue.Reader.TryRead(out var action))
                {
                    action();
                }

                if (_pumpCts.IsCancellationRequested)
                {
                    break;
                }

                Connection.FrameTick();
                Thread.Sleep(5);
            }

            while (_workQueue.Reader.TryRead(out var action))
            {
                action();
            }
        }

        public void Dispose()
        {
            _pumpCts.Cancel();
            _workQueue.Writer.Complete();

            // Deliberately unbounded - actor work items are quick (queue drains/registration, not
            // long-running calls), and joining unconditionally instead of on a fixed timeout means
            // Connection.Disconnect() below can never run concurrently with a live FrameTick() call
            // (the exact race a bounded, possibly-still-running join would have reintroduced).
            _actorThread.Join();
            Connection.Disconnect();
        }

        private sealed class Unregisterer : IDisposable
        {
            private readonly Action _unregister;

            public Unregisterer(Action unregister) => _unregister = unregister;

            public void Dispose() => _unregister();
        }
    }
}
