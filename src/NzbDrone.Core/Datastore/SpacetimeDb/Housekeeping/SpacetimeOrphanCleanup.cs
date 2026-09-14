using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NzbDrone.Core.Datastore.SpacetimeDb.Housekeeping
{
    // Shared shape for the "fetch child + parent tables, compute the orphan set in C#, delete via
    // the existing repository primitive" pattern SCHEMA_DESIGN.md's housekeeping section calls
    // for - SpacetimeDB has no equivalent of the real Housekeepers' raw
    // "DELETE ... WHERE Id IN (SELECT ... LEFT OUTER JOIN ... WHERE parent.Id IS NULL)" queries.
    internal static class SpacetimeOrphanCleanup
    {
        public static async Task DeleteWhereParentMissing<TChild>(
            IEnumerable<TChild> children,
            HashSet<int> validParentIds,
            Func<TChild, int> getParentId,
            Func<int, Task> delete)
            where TChild : ModelBase
        {
            foreach (var child in children.Where(c => !validParentIds.Contains(getParentId(c))).ToList())
            {
                await delete(child.Id);
            }
        }
    }
}
