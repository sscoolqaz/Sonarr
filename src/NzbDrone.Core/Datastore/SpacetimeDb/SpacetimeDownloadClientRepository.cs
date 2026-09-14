using System;
using NzbDrone.Core.Download;
using NzbDrone.Core.Messaging.Events;
using SpacetimeDB;
using EventContext = SpacetimeDB.Types.EventContext;
using ReducerEventContext = SpacetimeDB.Types.ReducerEventContext;
using StdbDownloadClientDefinition = SpacetimeDB.Types.DownloadClientDefinition;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeDownloadClientRepository : SpacetimeProviderRepository<DownloadClientDefinition, StdbDownloadClientDefinition>, IDownloadClientRepository
    {
        public SpacetimeDownloadClientRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override RemoteTableHandle<EventContext, StdbDownloadClientDefinition> Table => Conn.Connection.Db.DownloadClientDefinition;

        protected override StdbDownloadClientDefinition FindRowById(int id) => Conn.Connection.Db.DownloadClientDefinition.Id.Find(id);

        protected override IDisposable SubscribeOwnUpdateCommitted(Action<int> onCommitted, Action<Exception> onFailed)
        {
            void Handler(ReducerEventContext ctx, int id, string p2, string p3, string p4, string p5, bool p6, string p7, string p8)
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

            Conn.Connection.Reducers.OnUpdateDownloadClientDefinition += Handler;
            return new Unsubscriber(() => Conn.Connection.Reducers.OnUpdateDownloadClientDefinition -= Handler);
        }

        protected override DownloadClientDefinition ToModel(StdbDownloadClientDefinition row) => new DownloadClientDefinition
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

        protected override int GetRowId(StdbDownloadClientDefinition row) => row.Id;

        protected override void InvokeInsertReducer(DownloadClientDefinition model) => Conn.Connection.Reducers.InsertDownloadClientDefinition(
            model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        public override void MigrateInsert(DownloadClientDefinition model) => InvokeAndWaitForMigrateInsert(model.Id, () => Conn.Connection.Reducers.MigrateInsertDownloadClientDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message)));

        protected override void InvokeUpdateReducer(DownloadClientDefinition model) => Conn.Connection.Reducers.UpdateDownloadClientDefinition(
            model.Id, model.Name ?? string.Empty, model.Implementation ?? string.Empty, model.ConfigContract ?? string.Empty, SerializeSettings(model.Settings), model.Enable, SerializeTags(model.Tags), SerializeMessage(model.Message));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteDownloadClientDefinition(id);
    }
}
