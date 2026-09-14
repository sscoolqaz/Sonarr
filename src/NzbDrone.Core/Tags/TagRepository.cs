using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Tags
{
    public interface ITagRepository : IBasicRepository<Tag>
    {
        Task<Tag> GetByLabel(string label);
        Task<Tag> FindByLabel(string label);
        Task<List<Tag>> GetTags(HashSet<int> tagIds);
    }
}
