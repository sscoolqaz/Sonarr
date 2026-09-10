using System.Collections.Generic;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Profiles.Releases;
using StdbReleaseProfile = SpacetimeDB.Types.ReleaseProfile;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public class SpacetimeReleaseProfileRepository : SpacetimeBasicRepository<ReleaseProfile, StdbReleaseProfile>, IRestrictionRepository
    {
        public SpacetimeReleaseProfileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected override StdbReleaseProfile[] RemoteQuery(string whereClauseWithoutPrefix) =>
            Conn.Connection.Db.ReleaseProfile.RemoteQuery(whereClauseWithoutPrefix).GetAwaiter().GetResult();

        protected override ReleaseProfile ToModel(StdbReleaseProfile row) => new ReleaseProfile
        {
            Id = row.Id,
            Name = row.Name,
            Enabled = row.Enabled,
            Required = SpacetimeJson.Deserialize<List<string>>(row.RequiredJson) ?? new List<string>(),
            Ignored = SpacetimeJson.Deserialize<List<string>>(row.IgnoredJson) ?? new List<string>(),
            AirDateRestriction = row.AirDateRestriction,
            AirDateGracePeriod = row.AirDateGracePeriod,
            AllowSeasonPackWithoutAllEpisodesAired = row.AllowSeasonPackWithoutAllEpisodesAired,
            IndexerIds = SpacetimeJson.Deserialize<List<int>>(row.IndexerIdsJson) ?? new List<int>(),
            Tags = SpacetimeJson.Deserialize<HashSet<int>>(row.TagsJson) ?? new HashSet<int>(),
            ExcludedTags = SpacetimeJson.Deserialize<HashSet<int>>(row.ExcludedTagsJson) ?? new HashSet<int>()
        };

        protected override int GetRowId(StdbReleaseProfile row) => row.Id;

        protected override void InvokeInsertReducer(ReleaseProfile model) => Conn.Connection.Reducers.InsertReleaseProfile(
            model.Name ?? string.Empty,
            model.Enabled,
            SpacetimeJson.Serialize(model.Required),
            SpacetimeJson.Serialize(model.Ignored),
            model.AirDateRestriction,
            model.AirDateGracePeriod,
            model.AllowSeasonPackWithoutAllEpisodesAired,
            SpacetimeJson.Serialize(model.IndexerIds),
            SpacetimeJson.Serialize(model.Tags),
            SpacetimeJson.Serialize(model.ExcludedTags));

        protected override void InvokeUpdateReducer(ReleaseProfile model) => Conn.Connection.Reducers.UpdateReleaseProfile(
            model.Id,
            model.Name ?? string.Empty,
            model.Enabled,
            SpacetimeJson.Serialize(model.Required),
            SpacetimeJson.Serialize(model.Ignored),
            model.AirDateRestriction,
            model.AirDateGracePeriod,
            model.AllowSeasonPackWithoutAllEpisodesAired,
            SpacetimeJson.Serialize(model.IndexerIds),
            SpacetimeJson.Serialize(model.Tags),
            SpacetimeJson.Serialize(model.ExcludedTags));

        protected override void InvokeDeleteReducer(int id) => Conn.Connection.Reducers.DeleteReleaseProfile(id);
    }
}
