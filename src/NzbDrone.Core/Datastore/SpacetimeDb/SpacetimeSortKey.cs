using System;
using System.Reflection;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    // Real callers pass PagingSpec.SortKey values like "episodes.airDateUtc" (a dotted
    // table-qualified column name meant for the SQL sort the real repository builds). Two
    // genuinely different shapes hide behind that same syntax:
    //  - same-table: the prefix is T's own SQL table name (e.g. "episodes.airDateUtc" against
    //    Episode itself) - stripping to the last segment and resolving it directly on T handles
    //    this.
    //  - joined-table: the prefix names a genuinely different table (e.g. History/Blocklist's
    //    frontend-default "series.sortTitle", which the real repository's SQL join resolves
    //    server-side). There's no SpacetimeDB join to mirror that with, so it's resolved here
    //    against a nav property already attached on T by the caller (GetPaged/Since/etc already
    //    populate Series/Episode before sorting) - <NavPropertyName>.<PropertyName>, matched
    //    case-insensitively same as the same-table case.
    // Falls back to Id if the key is empty or doesn't match either shape, so an unrecognized/
    // differently-shaped sort key degrades to a stable default order rather than throwing.
    public static class SpacetimeSortKey
    {
        public static Func<T, object> Resolve<T>(string sortKey, Func<T, object> fallback)
        {
            if (string.IsNullOrEmpty(sortKey))
            {
                return fallback;
            }

            var dotIndex = sortKey.LastIndexOf('.');
            var propertyName = dotIndex >= 0 ? sortKey.Substring(dotIndex + 1) : sortKey;
            var property = typeof(T).GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            if (property != null)
            {
                return x => property.GetValue(x);
            }

            if (dotIndex > 0)
            {
                var navPropertyName = sortKey.Substring(0, dotIndex);
                var navProperty = typeof(T).GetProperty(navPropertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                var nestedProperty = navProperty?.PropertyType.GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

                if (nestedProperty != null)
                {
                    return x =>
                    {
                        var nav = navProperty.GetValue(x);
                        return nav != null ? nestedProperty.GetValue(nav) : null;
                    };
                }
            }

            return fallback;
        }
    }
}
