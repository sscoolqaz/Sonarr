using Newtonsoft.Json;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Storage-level JSON encoding for the embedded-document/collection fields that don't map
    /// onto a native SpacetimeDB column type (list/dictionary/custom-class/interface-typed
    /// fields) - the same role NzbDrone.Core.Datastore.Converters.EmbeddedDocumentConverter
    /// plays for the SQL-backed repositories, just targeting a plain string column instead of a
    /// SQL text column. Entirely internal to the storage layer - invisible to the REST API's own
    /// JSON contract, which still serializes the deserialized domain model the normal way.
    /// </summary>
    public static class SpacetimeJson
    {
        // TypeNameHandling stays None (the safe default) for the general case - only the
        // handful of genuinely polymorphic interface-typed fields (e.g.
        // List<ICustomFormatSpecification>) need type-preserving serialization, and those get
        // their own restricted handling rather than a blanket Auto here (CA2326/CA2327 flag
        // Auto as a real deserialization risk, correctly - this data does eventually originate
        // from REST API input, not just our own trusted writes).
        public static string Serialize<T>(T value) => JsonConvert.SerializeObject(value);

        public static T Deserialize<T>(string json) =>
            string.IsNullOrEmpty(json) ? default : JsonConvert.DeserializeObject<T>(json);
    }
}
