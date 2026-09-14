using System;
using NzbDrone.Core.Extras.Metadata;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbMetadataDefinition = SpacetimeDB.Types.MetadataDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeMetadataRepository : SpacetimeProviderRepository<MetadataDefinition, StdbMetadataDefinition>, IMetadataRepository
    {
        public SpacetimeMetadataRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbMetadataDefinition> Table => Conn.Connection.Db.MetadataDefinition;

        protected override StdbMetadataDefinition FindRowById(int id) => Conn.Connection.Db.MetadataDefinition.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4, string p5, bool p6, string p7, string p8)
            {
                if (ctx.Event.CallerIdentity == Conn.Connection.Identity &&
                    ctx.Event.CallerConnectionId == Conn.Connection.ConnectionId &&
                    ctx.Event.Status is Status.Committed)
                {
                    onCommitted(id);
                }
            }

            Conn.Connection.Reducers.OnUpdateMetadataDefinition += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateMetadataDefinition -= Handler);
        }

        protected override MetadataDefinition ToModel(StdbMetadataDefinition row) => new MetadataDefinition
        {
            Id = row.Id,
            Name = row.Name,
            Implementation = row.Implementation,
            ConfigContract = row.ConfigContract,
            Settings = DeserializeSettings(row.ConfigContract, row.SettingsJson),
            Enable = row.Enable,
            Tags = DeserializeTags(row.TagsJson),
            Message = DeserializeMessage(row.MessageJson)
        };

        protected override int GetRowId(StdbMetadataDefinition row) => row.Id;

        protected override void InvokeInsertReducer(MetadataDefinition model) => Conn.Connection.Reducers.InsertMetadataDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        public override void MigrateInsert(MetadataDefinition model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertMetadataDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message)));

        protected override void InvokeUpdateReducer(MetadataDefinition model) => Conn.Connection.Reducers.UpdateMetadataDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteMetadataDefinition(id);
    }
}
