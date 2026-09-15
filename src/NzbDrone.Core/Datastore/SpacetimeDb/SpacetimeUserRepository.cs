using System;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbUser = SpacetimeDB.Types.User;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeUserRepository : SpacetimeBasicRepository<User, StdbUser>, IUserRepository
    {
        public SpacetimeUserRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        // Reads go through the TrustedUsers view (Sonarr.SpacetimeModule/spacetimedb/Batch1.cs),
        // not the User table directly - User is a private table now, readable only by the
        // module's own reducers. TrustedUsers gates on TrustedConnection membership server-side
        // and exposes the same row shape/primary key as User, so the generated view handle here
        // (TrustedUsersHandle : RemoteTableHandle<EventContext, StdbUser>) is a drop-in
        // replacement for the old direct table handle. Writes are unaffected - they still go
        // through the InsertUser/UpdateUser/DeleteUser reducers below, which mutate the real
        // (private) User table.
        protected override RemoteTableHandle<EventContext, StdbUser> Table => Conn.Connection.Db.TrustedUsers;

        protected override StdbUser FindRowById(int id) => Conn.Connection.Db.TrustedUsers.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4, string p5, int p6)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnUpdateUser += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateUser -= Handler);
        }

        protected override IDisposable SubscribeOwnDeleteCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnDeleteUser += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnDeleteUser -= Handler);
        }

        protected override IDisposable SubscribeOwnInsertCommitted(Action onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, string p1, string p2, string p3, string p4, int p5)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted();
                }
                else if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                         ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                         (ctx.Event.Status is Status.Failed || ctx.Event.Status is Status.OutOfEnergy))
                {
                    onFailed(new InvalidOperationException($"Reducer failed with status {ctx.Event.Status}"));
                }
            }

            Conn.Connection.Reducers.OnInsertUser += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnInsertUser -= Handler);
        }

        protected override User ToModel(StdbUser row) => new User
        {
            Id = row.Id,
            Identifier = Guid.Parse(row.Identifier),
            Username = row.Username,
            Password = row.Password,
            Salt = row.Salt,
            Iterations = row.Iterations
        };

        protected override int GetRowId(StdbUser row) => row.Id;

        protected override void InvokeInsertReducer(User model) =>
            Conn.Connection.Reducers.InsertUser(model.Identifier.ToString(), model.Username ?? string.Empty, model.Password ?? string.Empty, model.Salt ?? string.Empty, model.Iterations);

        public override Task MigrateInsert(User model) =>
            InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertUser(model.Id, model.Identifier.ToString(), model.Username ?? string.Empty, model.Password ?? string.Empty, model.Salt ?? string.Empty, model.Iterations));

        protected override void InvokeUpdateReducer(User model) =>
            Conn.Connection.Reducers.UpdateUser(model.Id, model.Identifier.ToString(), model.Username ?? string.Empty, model.Password ?? string.Empty, model.Salt ?? string.Empty, model.Iterations);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteUser(id);

        public Task<User> FindUser(string username) =>
            Query(t => t.Iter().Where(r => r.Username == username).Select(ToModel).SingleOrDefault());

        public Task<User> FindUser(Guid identifier)
        {
            var identifierString = identifier.ToString();
            return Query(t => t.Iter().Where(r => r.Identifier == identifierString).Select(ToModel).SingleOrDefault());
        }
    }
}
