using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbTag = SpacetimeDB.Types.Tag;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// SpacetimeDB-backed ITagRepository, now built on the generic SpacetimeBasicRepository base
    /// (see that class's remarks) instead of the hand-rolled Phase 1 spike implementation. Tag
    /// itself is unchanged in behavior - this refactor exists to prove the generic base against
    /// an entity already validated end-to-end through the real TagService and REST API, before
    /// applying the same base to Phase 4's new entities.
    /// </summary>
    public class SpacetimeTagRepository : SpacetimeBasicRepository<Tag, StdbTag>, ITagRepository
    {
        public SpacetimeTagRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbTag> Table => Conn.Connection.Db.Tag;

        protected override StdbTag FindRowById(int id) => Conn.Connection.Db.Tag.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2)
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

            Conn.Connection.Reducers.OnUpdateTag += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateTag -= Handler);
        }

        protected override Tag ToModel(StdbTag row) => new Tag { Id = row.Id, Label = row.Label };

        protected override int GetRowId(StdbTag row) => row.Id;

        protected override void InvokeInsertReducer(Tag model) => Conn.Connection.Reducers.InsertTag(model.Label ?? string.Empty);

        public override Task MigrateInsert(Tag model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertTag(model.Id, model.Label ?? string.Empty));

        protected override void InvokeUpdateReducer(Tag model) => Conn.Connection.Reducers.UpdateTag(model.Id, model.Label ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteTag(id);

        protected override object GetSortKey(Tag model) => model.Label;

        public async Task<Tag> GetByLabel(string label)
        {
            var model = await FindByLabel(label);

            if (model == null)
            {
                throw new InvalidOperationException("Didn't find tag with label " + label);
            }

            return model;
        }

        public Task<Tag> FindByLabel(string label) =>
            Query(t => t.Iter().Where(r => r.Label == label).Select(ToModel).SingleOrDefault());

        public async Task<List<Tag>> GetTags(HashSet<int> tagIds) =>
            (await All()).Where(t => tagIds.Contains(t.Id)).ToList();
    }
}
