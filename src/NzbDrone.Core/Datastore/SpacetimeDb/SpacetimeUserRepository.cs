using System;
using System.Linq;
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

        protected override RemoteTableHandle<EventContext, StdbUser> Table => Conn.Connection.Db.User;

        protected override StdbUser FindRowById(int id) => Conn.Connection.Db.User.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4, string p5, int p6)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateUser += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateUser -= Handler);
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

        public override void MigrateInsert(User model) =>
            InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertUser(model.Id, model.Identifier.ToString(), model.Username ?? string.Empty, model.Password ?? string.Empty, model.Salt ?? string.Empty, model.Iterations));

        protected override void InvokeUpdateReducer(User model) =>
            Conn.Connection.Reducers.UpdateUser(model.Id, model.Identifier.ToString(), model.Username ?? string.Empty, model.Password ?? string.Empty, model.Salt ?? string.Empty, model.Iterations);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteUser(id);

        public User FindUser(string username) =>
            Query(t => t.Iter().Where(r => r.Username == username).Select(ToModel).SingleOrDefault());

        public User FindUser(Guid identifier)
        {
            var identifierString = identifier.ToString();
            return Query(t => t.Iter().Where(r => r.Identifier == identifierString).Select(ToModel).SingleOrDefault());
        }
    }
}
