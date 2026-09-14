using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Blocklisting
{
    public interface IBlocklistRepository : IBasicRepository<Blocklist>
    {
        Task<List<Blocklist>> BlocklistedByTitle(int seriesId, string sourceTitle);
        Task<List<Blocklist>> BlocklistedByTorrentInfoHash(int seriesId, string torrentInfoHash);
        Task<List<Blocklist>> BlocklistedBySeries(int seriesId);
        Task DeleteForSeriesIds(List<int> seriesIds);
    }
}
