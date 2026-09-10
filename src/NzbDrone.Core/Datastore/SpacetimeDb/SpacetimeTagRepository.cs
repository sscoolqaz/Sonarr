using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
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

        protected override StdbTag[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.Tag.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override Tag ToModel(StdbTag row) => new Tag { Id = row.Id, Label = row.Label };

        protected override int GetRowId(StdbTag row) => row.Id;

        protected override void InvokeInsertReducer(Tag model) => Conn.Connection.Reducers.InsertTag(model.Label ?? string.Empty);

        protected override void InvokeUpdateReducer(Tag model) => Conn.Connection.Reducers.UpdateTag(model.Id, model.Label ?? string.Empty);

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteTag(id);

        protected override object GetSortKey(Tag model) => model.Label;

        public Tag GetByLabel(string label)
        {
            var model = FindByLabel(label);

            if (model == null)
            {
                throw new InvalidOperationException("Didn't find tag with label " + label);
            }

            return model;
        }

        public Tag FindByLabel(string label)
        {
            var rows = RemoteQuery($"WHERE Label = '{EscapeSqlString(label)}'");
            return rows.Select(ToModel).SingleOrDefault();
        }

        public List<Tag> GetTags(HashSet<int> tagIds) =>
            All().Where(t => tagIds.Contains(t.Id)).ToList();
    }
}
