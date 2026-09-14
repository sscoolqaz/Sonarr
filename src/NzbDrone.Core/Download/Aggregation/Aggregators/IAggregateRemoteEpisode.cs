using System.Threading.Tasks;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Aggregation.Aggregators
{
    public interface IAggregateRemoteEpisode
    {
        Task<RemoteEpisode> Aggregate(RemoteEpisode remoteEpisode);
    }
}
