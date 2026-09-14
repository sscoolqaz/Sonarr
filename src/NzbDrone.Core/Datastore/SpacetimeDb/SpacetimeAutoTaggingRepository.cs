using System;
using System.Collections.Generic;
using System.Text.Json;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbAutoTag = SpacetimeDB.Types.AutoTag;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeAutoTaggingRepository : SpacetimeBasicRepository<AutoTag, StdbAutoTag>, IAutoTaggingRepository
    {
        // Same reasoning as SpacetimeCustomFormatRepository - reuse the existing
        // assembly-restricted polymorphic converter rather than a blanket TypeNameHandling.Auto.
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            Converters = { new AutoTaggingSpecificationConverter() }
        };

        public SpacetimeAutoTaggingRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbAutoTag> Table => Conn.Connection.Db.AutoTag;

        protected override StdbAutoTag FindRowById(int id) => Conn.Connection.Db.AutoTag.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, bool p4, string p5)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateAutoTag += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateAutoTag -= Handler);
        }

        protected override AutoTag ToModel(StdbAutoTag row) => new AutoTag
        {
            Id = row.Id,
            Name = row.Name,
            Specifications = string.IsNullOrEmpty(row.SpecificationsJson)
                ? new List<IAutoTaggingSpecification>()
                : JsonSerializer.Deserialize<List<IAutoTaggingSpecification>>(row.SpecificationsJson, Options),
            RemoveTagsAutomatically = row.RemoveTagsAutomatically,
            Tags = SpacetimeJson.Deserialize<HashSet<int>>(row.TagsJson) ?? new HashSet<int>()
        };

        protected override int GetRowId(StdbAutoTag row) => row.Id;

        protected override void InvokeInsertReducer(AutoTag model) => Conn.Connection.Reducers.InsertAutoTag(
            model.Name ?? string.Empty, JsonSerializer.Serialize(model.Specifications, Options), model.RemoveTagsAutomatically, SpacetimeJson.Serialize(model.Tags));

        public override void MigrateInsert(AutoTag model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertAutoTag(
            model.Id, model.Name ?? string.Empty, JsonSerializer.Serialize(model.Specifications, Options), model.RemoveTagsAutomatically, SpacetimeJson.Serialize(model.Tags)));

        protected override void InvokeUpdateReducer(AutoTag model) => Conn.Connection.Reducers.UpdateAutoTag(
            model.Id, model.Name ?? string.Empty, JsonSerializer.Serialize(model.Specifications, Options), model.RemoveTagsAutomatically, SpacetimeJson.Serialize(model.Tags));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteAutoTag(id);
    }
}
