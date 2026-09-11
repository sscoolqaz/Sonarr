using System;
using System.Reflection;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    // Real callers pass PagingSpec.SortKey values like "episodes.airDateUtc" (a dotted
    // table-qualified column name meant for the SQL sort the real repository builds) - strip
    // everything up to the last '.' before resolving it as a property name here, and fall back
    // to Id if the key is empty or doesn't match any property, so an unrecognized/differently-
    // shaped sort key degrades to a stable default order rather than throwing.
    public static class SpacetimeSortKey
    {
        public static Func<T, object> Resolve<T>(string sortKey, Func<T, object> fallback)
        {
            if (string.IsNullOrEmpty(sortKey))
            {
                return fallback;
            }

            var propertyName = sortKey.Contains('.') ? sortKey.Substring(sortKey.LastIndexOf('.') + 1) : sortKey;
            var property = typeof(T).GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            return property != null ? (x => property.GetValue(x)) : fallback;
        }
    }
}
