using System.Threading.Tasks;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerRepository : IProviderRepository<IndexerDefinition>
    {
        Task<IndexerDefinition> FindByName(string name);
    }
}
