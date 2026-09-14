using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Extras
{
    public interface IExistingExtraFiles
    {
        Task<List<string>> ImportExtraFiles(Series series, List<string> possibleExtraFiles, string fileNameBeforeRename);
    }

    public class ExistingExtraFileService : IExistingExtraFiles, IHandle<SeriesScannedEvent>
    {
        private readonly List<IImportExistingExtraFiles> _existingExtraFileImporters;
        private readonly Logger _logger;

        public ExistingExtraFileService(IEnumerable<IImportExistingExtraFiles> existingExtraFileImporters,
                                        Logger logger)
        {
            _existingExtraFileImporters = existingExtraFileImporters.OrderBy(e => e.Order).ToList();
            _logger = logger;
        }

        public async Task<List<string>> ImportExtraFiles(Series series, List<string> possibleExtraFiles, string fileNameBeforeRename)
        {
            _logger.Debug("Looking for existing extra files in {0}", series.Path);

            var importedFiles = new List<string>();

            foreach (var existingExtraFileImporter in _existingExtraFileImporters)
            {
                var imported = await existingExtraFileImporter.ProcessFiles(series, possibleExtraFiles, importedFiles, fileNameBeforeRename);

                importedFiles.AddRange(imported.Select(f => Path.Combine(series.Path, f.RelativePath)));
            }

            return importedFiles;
        }

        // IHandle<T> is a shared, synchronous, app-wide eventing interface - its signature can't change here.
        // Bridging with GetAwaiter().GetResult() is safe: EventAggregator runs handlers off the request thread
        // via Task.Factory.StartNew, and ASP.NET Core carries no SynchronizationContext.
        public void Handle(SeriesScannedEvent message)
        {
            var series = message.Series;
            var possibleExtraFiles = message.PossibleExtraFiles;
            var importedFiles = ImportExtraFiles(series, possibleExtraFiles, null).GetAwaiter().GetResult();

            _logger.Info("Found {0} possible extra files, imported {1} files.", possibleExtraFiles.Count, importedFiles.Count);
        }
    }
}
