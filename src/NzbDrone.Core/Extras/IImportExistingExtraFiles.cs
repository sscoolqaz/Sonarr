using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Extras
{
    public interface IImportExistingExtraFiles
    {
        int Order { get; }
        Task<IEnumerable<ExtraFile>> ProcessFiles(Series series, List<string> filesOnDisk, List<string> importedFiles, string fileNameBeforeRename);
    }
}
