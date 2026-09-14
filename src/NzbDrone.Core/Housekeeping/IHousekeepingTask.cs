using System.Threading.Tasks;

namespace NzbDrone.Core.Housekeeping
{
    public interface IHousekeepingTask
    {
        Task Clean();
    }
}
