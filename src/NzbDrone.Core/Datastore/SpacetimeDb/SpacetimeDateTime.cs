using System;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// SpacetimeDB has no native DateTime column type - SpacetimeDB.Timestamp (implicitly
    /// convertible to/from DateTimeOffset) is the real column type. Sonarr's domain models use
    /// plain UTC DateTime throughout, so these two helpers are the one place that conversion
    /// happens, rather than repeating it at every call site.
    /// </summary>
    public static class SpacetimeDateTime
    {
        public static SpacetimeDB.Timestamp ToTimestamp(DateTime value) =>
            (SpacetimeDB.Timestamp)new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

        public static DateTime ToDateTime(SpacetimeDB.Timestamp value) => ((DateTimeOffset)value).UtcDateTime;

        public static SpacetimeDB.Timestamp? ToTimestamp(DateTime? value) => value.HasValue ? ToTimestamp(value.Value) : null;

        public static DateTime? ToDateTime(SpacetimeDB.Timestamp? value) => value.HasValue ? ToDateTime(value.Value) : null;
    }
}
