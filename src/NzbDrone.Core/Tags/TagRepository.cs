using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags
{
    public interface ITagRepository : IBasicRepository<Tag>
    {
        Tag GetByLabel(string label);
        Tag FindByLabel(string label);
        List<Tag> GetTags(HashSet<int> tagIds);
    }
}
