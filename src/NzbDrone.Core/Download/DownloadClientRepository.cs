using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Download
{
    public interface IDownloadClientRepository : IProviderRepository<DownloadClientDefinition>
    {
    }
}
