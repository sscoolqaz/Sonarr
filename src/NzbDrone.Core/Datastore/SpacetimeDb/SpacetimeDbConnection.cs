using System.Threading;
using System.Threading.Tasks;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// One shared SpacetimeDB connection for the whole app, injected into every
    /// SpacetimeBasicRepository&lt;,&gt; instance instead of each repository owning its own
    /// connection (which is what the Phase 1 spike did - fine for one entity, wasteful at the
    /// ~40-entity scale Phase 4 ports).
    ///
    /// Owns the background FrameTick pump thread required for reducer/subscription callbacks
    /// (see SpacetimeTagRepository's original remarks - RemoteQuery reads don't need this pump,
    /// only callback-driven event delivery does). Runs for the lifetime of the DI container.
    /// </summary>
    public interface ISpacetimeDbConnection
    {
        SpacetimeDB.Types.DbConnection Connection { get; }
    }

    public class SpacetimeDbConnection : ISpacetimeDbConnection, System.IDisposable
    {
        private readonly Thread _pumpThread;
        private readonly CancellationTokenSource _pumpCts = new CancellationTokenSource();

        public SpacetimeDB.Types.DbConnection Connection { get; }

        public SpacetimeDbConnection(string host, string database)
        {
            var connectedTcs = new TaskCompletionSource<SpacetimeDB.Types.DbConnection>();

            Connection = SpacetimeDB.Types.DbConnection.Builder()
                .WithUri(host)
                .WithDatabaseName(database)
                .OnConnect((c, identity, token) => connectedTcs.TrySetResult(c))
                .OnConnectError(err => connectedTcs.TrySetException(err))
                .OnDisconnect((c, err) => { })
                .Build();

            _pumpThread = new Thread(PumpLoop)
            {
                IsBackground = true,
                Name = "SpacetimeDB-FrameTick-Pump"
            };
            _pumpThread.Start();

            connectedTcs.Task.GetAwaiter().GetResult();
        }

        private void PumpLoop()
        {
            while (!_pumpCts.IsCancellationRequested)
            {
                Connection.FrameTick();
                Thread.Sleep(20);
            }
        }

        public void Dispose()
        {
            _pumpCts.Cancel();
            Connection.Disconnect();
        }
    }
}
