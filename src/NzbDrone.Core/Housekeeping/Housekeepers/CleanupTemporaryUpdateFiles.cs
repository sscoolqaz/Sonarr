using System.Threading.Tasks;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupTemporaryUpdateFiles : IHousekeepingTask
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IAppFolderInfo _appFolderInfo;

        public CleanupTemporaryUpdateFiles(IDiskProvider diskProvider, IAppFolderInfo appFolderInfo)
        {
            _diskProvider = diskProvider;
            _appFolderInfo = appFolderInfo;
        }

        public Task Clean()
        {
            var updateSandboxFolder = _appFolderInfo.GetUpdateSandboxFolder();

            if (_diskProvider.FolderExists(updateSandboxFolder))
            {
                _diskProvider.DeleteFolder(updateSandboxFolder, true);
            }

            return Task.CompletedTask;
        }
    }
}
