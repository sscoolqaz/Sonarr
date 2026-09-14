using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.RootFolders
{
    public interface IRootFolderService
    {
        Task<List<RootFolder>> All();
        Task<List<RootFolder>> AllWithUnmappedFolders();
        Task<RootFolder> Add(RootFolder rootDir);
        Task Remove(int id);
        Task<RootFolder> Get(int id, bool timeout);
        Task<string> GetBestRootFolderPath(string path);
    }

    public class RootFolderService : IRootFolderService
    {
        private readonly IRootFolderRepository _rootFolderRepository;
        private readonly IDiskProvider _diskProvider;
        private readonly ISeriesRepository _seriesRepository;
        private readonly INamingConfigService _namingConfigService;
        private readonly Logger _logger;

        private readonly ICached<string> _cache;

        private static readonly HashSet<string> SpecialFolders = new HashSet<string>
                                                                 {
                                                                     "$recycle.bin",
                                                                     "system volume information",
                                                                     "recycler",
                                                                     "lost+found",
                                                                     ".appledb",
                                                                     ".appledesktop",
                                                                     ".appledouble",
                                                                     "@eadir",
                                                                     ".grab"
                                                                 };

        public RootFolderService(IRootFolderRepository rootFolderRepository,
                                 IDiskProvider diskProvider,
                                 ISeriesRepository seriesRepository,
                                 INamingConfigService namingConfigService,
                                 ICacheManager cacheManager,
                                 Logger logger)
        {
            _rootFolderRepository = rootFolderRepository;
            _diskProvider = diskProvider;
            _seriesRepository = seriesRepository;
            _namingConfigService = namingConfigService;
            _logger = logger;

            _cache = cacheManager.GetCache<string>(GetType());
        }

        public async Task<List<RootFolder>> All()
        {
            var rootFolders = (await _rootFolderRepository.All()).ToList();

            return rootFolders;
        }

        public async Task<List<RootFolder>> AllWithUnmappedFolders()
        {
            var rootFolders = (await _rootFolderRepository.All()).ToList();
            var seriesPaths = await _seriesRepository.AllSeriesPaths();
            var namingConfig = await _namingConfigService.GetConfig();

            rootFolders.ForEach(folder =>
            {
                try
                {
                    if (folder.Path.IsPathValid(PathValidationType.CurrentOs))
                    {
                        GetDetails(folder, seriesPaths, namingConfig, true);
                    }
                }

                // We don't want an exception to prevent the root folders from loading in the UI, so they can still be deleted
                catch (Exception ex)
                {
                    _logger.Error(ex, "Unable to get free space and unmapped folders for root folder {0}", folder.Path);
                    folder.UnmappedFolders = new List<UnmappedFolder>();
                }
            });

            return rootFolders;
        }

        public async Task<RootFolder> Add(RootFolder rootFolder)
        {
            var all = await All();

            if (string.IsNullOrWhiteSpace(rootFolder.Path) || !Path.IsPathRooted(rootFolder.Path))
            {
                throw new ArgumentException("Invalid path");
            }

            if (!_diskProvider.FolderExists(rootFolder.Path))
            {
                throw new DirectoryNotFoundException("Can't add root directory that doesn't exist.");
            }

            if (all.Exists(r => r.Path.PathEquals(rootFolder.Path)))
            {
                throw new InvalidOperationException("Recent directory already exists.");
            }

            if (!_diskProvider.FolderWritable(rootFolder.Path))
            {
                throw new UnauthorizedAccessException($"Root folder path '{rootFolder.Path}' is not writable by user '{Environment.UserName}'");
            }

            await _rootFolderRepository.Insert(rootFolder);
            var seriesPaths = await _seriesRepository.AllSeriesPaths();
            var namingConfig = await _namingConfigService.GetConfig();

            GetDetails(rootFolder, seriesPaths, namingConfig, true);
            _cache.Clear();

            return rootFolder;
        }

        public async Task Remove(int id)
        {
            await _rootFolderRepository.Delete(id);
            _cache.Clear();
        }

        private List<UnmappedFolder> GetUnmappedFolders(string path, Dictionary<int, string> seriesPaths, NamingConfig namingConfig)
        {
            _logger.Debug("Generating list of unmapped folders");

            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Invalid path provided", nameof(path));
            }

            var results = new List<UnmappedFolder>();

            if (!_diskProvider.FolderExists(path))
            {
                _logger.Debug("Path supplied does not exist: {0}", path);
                return results;
            }

            var subFolderDepth = namingConfig.SeriesFolderFormat.Count(f => f == Path.DirectorySeparatorChar);
            var possibleSeriesFolders = _diskProvider.GetDirectories(path).ToList();

            if (subFolderDepth > 0)
            {
                for (var i = 0; i < subFolderDepth; i++)
                {
                    possibleSeriesFolders = possibleSeriesFolders.SelectMany(_diskProvider.GetDirectories).ToList();
                }
            }

            var unmappedFolders = possibleSeriesFolders.Except(seriesPaths.Select(s => s.Value), PathEqualityComparer.Instance).ToList();

            foreach (var unmappedFolder in unmappedFolders)
            {
                var di = new DirectoryInfo(unmappedFolder.Normalize());
                results.Add(new UnmappedFolder
                {
                    Name = di.Name,
                    Path = di.FullName,
                    RelativePath = path.GetRelativePath(di.FullName)
                });
            }

            var setToRemove = SpecialFolders;
            results.RemoveAll(x => setToRemove.Contains(new DirectoryInfo(x.Path.ToLowerInvariant()).Name));

            _logger.Debug("{0} unmapped folders detected.", results.Count);
            return results.OrderBy(u => u.Name, StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        public async Task<RootFolder> Get(int id, bool timeout)
        {
            var rootFolder = await _rootFolderRepository.Get(id);
            var seriesPaths = await _seriesRepository.AllSeriesPaths();
            var namingConfig = await _namingConfigService.GetConfig();

            GetDetails(rootFolder, seriesPaths, namingConfig, timeout);

            return rootFolder;
        }

        public async Task<string> GetBestRootFolderPath(string path)
        {
            var cached = _cache.Find(path);

            if (cached != null)
            {
                return cached;
            }

            var result = await GetBestRootFolderPathInternal(path);
            _cache.Set(path, result, TimeSpan.FromDays(1));

            return result;
        }

        // NOTE: this already wraps disk I/O in Task.Run(...).Wait(timeout) as a deliberate
        // hard-timeout mechanism for slow/hung network mounts, unrelated to the repository async
        // conversion; naming config and series paths are now fetched by the callers beforehand
        // (rather than awaited from inside this timed block) to keep that timeout semantics intact.
        private void GetDetails(RootFolder rootFolder, Dictionary<int, string> seriesPaths, NamingConfig namingConfig, bool timeout)
        {
            Task.Run(() =>
            {
                if (_diskProvider.FolderExists(rootFolder.Path))
                {
                    rootFolder.Accessible = true;
                    rootFolder.IsEmpty = _diskProvider.FolderEmpty(rootFolder.Path);
                    rootFolder.FreeSpace = _diskProvider.GetAvailableSpace(rootFolder.Path);
                    rootFolder.TotalSpace = _diskProvider.GetTotalSize(rootFolder.Path);
                    rootFolder.UnmappedFolders = GetUnmappedFolders(rootFolder.Path, seriesPaths, namingConfig);
                }
            }).Wait(timeout ? 5000 : -1);
        }

        private async Task<string> GetBestRootFolderPathInternal(string path)
        {
            var possibleRootFolder = (await All()).Where(r => r.Path.IsParentPath(path)).MaxBy(r => r.Path.Length);

            if (possibleRootFolder == null)
            {
                var osPath = new OsPath(path);

                return osPath.Directory.ToString().GetCleanPath();
            }

            return possibleRootFolder.Path.GetCleanPath();
        }
    }
}
