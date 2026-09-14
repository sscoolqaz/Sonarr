using System.Collections.Generic;
using Newtonsoft.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Reflection;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Generic base mirroring the real ProviderRepository&lt;TProviderDefinition&gt; (shared by
    /// Indexer/ImportList/Notification/DownloadClient/Metadata *definitions*, Tier 1.5 per the
    /// schema design - not their status tables, see SpacetimeProviderStatusRepository for those).
    ///
    /// Settings is a JSON blob whose .NET type depends on ConfigContract - resolved the same way
    /// the real repository does it, via NzbDrone.Common.Reflection.FindTypeByName restricted to
    /// the IProviderConfig assembly (not an arbitrary type lookup), just using Newtonsoft here
    /// instead of System.Text.Json since this is purely internal storage, not required to match
    /// the SQL repository's exact serialized bytes.
    /// </summary>
    public abstract class SpacetimeProviderRepository<TProviderDefinition, TStdbRow> : SpacetimeBasicRepository<TProviderDefinition, TStdbRow>
        where TProviderDefinition : ProviderDefinition, new()
        where TStdbRow : class, SpacetimeDB.BSATN.IStructuralReadWrite, new()
    {
        protected SpacetimeProviderRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        protected static string SerializeSettings(IProviderConfig settings) =>
            settings == null ? string.Empty : JsonConvert.SerializeObject(settings);

        protected static IProviderConfig DeserializeSettings(string configContract, string settingsJson)
        {
            var implementationType = typeof(IProviderConfig).Assembly.FindTypeByName(configContract);

            if (settingsJson.IsNullOrWhiteSpace() || implementationType == null)
            {
                return NullConfig.Instance;
            }

            return (IProviderConfig)JsonConvert.DeserializeObject(settingsJson, implementationType);
        }

        protected static string SerializeTags(HashSet<int> tags) => SpacetimeJson.Serialize(tags);

        protected static HashSet<int> DeserializeTags(string tagsJson) => SpacetimeJson.Deserialize<HashSet<int>>(tagsJson) ?? new HashSet<int>();

        protected static string SerializeMessage(ProviderMessage message) => SpacetimeJson.Serialize(message);

        protected static ProviderMessage DeserializeMessage(string messageJson) => SpacetimeJson.Deserialize<ProviderMessage>(messageJson);
    }
}
