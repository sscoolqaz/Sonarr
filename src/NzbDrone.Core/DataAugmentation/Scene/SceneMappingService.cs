using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.DataAugmentation.Scene
{
    public interface ISceneMappingService
    {
        Task<List<string>> GetSceneNames(int tvdbId, List<int> seasonNumbers, List<int> sceneSeasonNumbers);
        Task<int?> FindTvdbId(string sceneTitle, string releaseTitle, int sceneSeasonNumber);
        Task<List<SceneMapping>> FindByTvdbId(int tvdbId);
        Task<SceneMapping> FindSceneMapping(string sceneTitle, string releaseTitle, int sceneSeasonNumber);
        Task<int?> GetSceneSeasonNumber(string seriesTitle, string releaseTitle);
    }

    public class SceneMappingService : ISceneMappingService,
                                       IHandle<SeriesRefreshStartingEvent>,
                                       IHandle<SeriesAddedEvent>,
                                       IHandle<SeriesImportedEvent>,
                                       IExecute<UpdateSceneMappingCommand>
    {
        private readonly ISceneMappingRepository _repository;
        private readonly IEnumerable<ISceneMappingProvider> _sceneMappingProviders;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly ICachedDictionary<List<SceneMapping>> _getTvdbIdCache;
        private readonly ICachedDictionary<List<SceneMapping>> _findByTvdbIdCache;
        private bool _updatedAfterStartup;

        public SceneMappingService(ISceneMappingRepository repository,
                                   ICacheManager cacheManager,
                                   IEnumerable<ISceneMappingProvider> sceneMappingProviders,
                                   IEventAggregator eventAggregator,
                                   Logger logger)
        {
            _repository = repository;
            _sceneMappingProviders = sceneMappingProviders;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _getTvdbIdCache = cacheManager.GetCacheDictionary<List<SceneMapping>>(GetType(), "tvdb_id");
            _findByTvdbIdCache = cacheManager.GetCacheDictionary<List<SceneMapping>>(GetType(), "find_tvdb_id");
        }

        public async Task<List<string>> GetSceneNames(int tvdbId, List<int> seasonNumbers, List<int> sceneSeasonNumbers)
        {
            var mappings = await FindByTvdbId(tvdbId);

            if (mappings == null)
            {
                return new List<string>();
            }

            var names = mappings.Where(n => seasonNumbers.Contains(n.SeasonNumber ?? -1) ||
                                            sceneSeasonNumbers.Contains(n.SceneSeasonNumber ?? -1) ||
                                            ((n.SeasonNumber ?? -1) == -1 && (n.SceneSeasonNumber ?? -1) == -1 && n.SceneOrigin != "tvdb"))
                                .Select(n => n.SearchTerm)
                                .Distinct(StringComparer.InvariantCultureIgnoreCase)
                                .ToList();

            return names;
        }

        public async Task<int?> FindTvdbId(string seriesTitle, string releaseTitle, int sceneSeasonNumber)
        {
            return (await FindSceneMapping(seriesTitle, releaseTitle, sceneSeasonNumber))?.TvdbId;
        }

        public async Task<List<SceneMapping>> FindByTvdbId(int tvdbId)
        {
            if (_findByTvdbIdCache.Count == 0)
            {
                await RefreshCache();
            }

            var mappings = _findByTvdbIdCache.Find(tvdbId.ToString());

            if (mappings == null)
            {
                return new List<SceneMapping>();
            }

            return mappings;
        }

        public async Task<SceneMapping> FindSceneMapping(string seriesTitle, string releaseTitle, int sceneSeasonNumber)
        {
            if (seriesTitle.IsNullOrWhiteSpace())
            {
                return null;
            }

            var mappings = await FindMappings(seriesTitle, releaseTitle);

            if (mappings == null)
            {
                return null;
            }

            mappings = FilterSceneMappings(mappings, sceneSeasonNumber);

            var distinctMappings = mappings.DistinctBy(v => v.TvdbId).ToList();

            if (distinctMappings.Count == 0)
            {
                return null;
            }

            if (distinctMappings.Count == 1)
            {
                var mapping = distinctMappings.First();
                _logger.Debug("Found scene mapping for: {0}. TVDB ID for mapping: {1}", seriesTitle, mapping.TvdbId);
                return distinctMappings.First();
            }

            throw new InvalidSceneMappingException(mappings, releaseTitle);
        }

        public async Task<int?> GetSceneSeasonNumber(string seriesTitle, string releaseTitle)
        {
            return (await FindSceneMapping(seriesTitle, releaseTitle, -1))?.SceneSeasonNumber;
        }

        private async Task UpdateMappings()
        {
            _logger.Info("Updating Scene mappings");

            _updatedAfterStartup = true;

            foreach (var sceneMappingProvider in _sceneMappingProviders)
            {
                try
                {
                    var mappings = sceneMappingProvider.GetSceneMappings();

                    if (mappings.Any())
                    {
                        var providerType = sceneMappingProvider.GetType().Name;

                        mappings.RemoveAll(sceneMapping =>
                        {
                            if (sceneMapping.Title.IsNullOrWhiteSpace() ||
                                sceneMapping.SearchTerm.IsNullOrWhiteSpace())
                            {
                                _logger.Warn("Invalid scene mapping found for: {0}, skipping", sceneMapping.TvdbId);
                                return true;
                            }

                            return false;
                        });

                        foreach (var sceneMapping in mappings)
                        {
                            sceneMapping.ParseTerm = sceneMapping.Title.CleanSeriesTitle();
                            sceneMapping.Type = providerType;
                        }

                        var existing = await _repository.GetAllByType(providerType);
                        var existingByMappingId = new Dictionary<string, SceneMapping>();

                        foreach (var e in existing)
                        {
                            existingByMappingId[e.MappingId ?? $"{e.Id}"] = e;
                        }

                        var toInsert = new List<SceneMapping>();
                        var toUpdate = new List<SceneMapping>();

                        foreach (var mapping in mappings)
                        {
                            if (mapping.MappingId.IsNullOrWhiteSpace())
                            {
                                _logger.Warn("Scene mapping with missing MappingId found for: {0} {1}, skipping", mapping.TvdbId, mapping.Title);
                                continue;
                            }

                            if (existingByMappingId.TryGetValue(mapping.MappingId, out var existingMapping))
                            {
                                mapping.Id = existingMapping.Id;
                                toUpdate.Add(mapping);
                                existingByMappingId.Remove(mapping.MappingId);
                            }
                            else
                            {
                                toInsert.Add(mapping);
                            }
                        }

                        var toDelete = existingByMappingId.Values.ToList();

                        await _repository.DeleteMany(toDelete);
                        await _repository.UpdateMany(toUpdate);
                        await _repository.InsertMany(toInsert);
                    }
                    else
                    {
                        _logger.Warn("Received empty list of mapping. will not update");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to Update Scene Mappings");
                }
            }

            await RefreshCache();

            _eventAggregator.PublishEvent(new SceneMappingsUpdatedEvent());
        }

        private async Task<List<SceneMapping>> FindMappings(string seriesTitle, string releaseTitle)
        {
            if (_getTvdbIdCache.Count == 0)
            {
                await RefreshCache();
            }

            var candidates = _getTvdbIdCache.Find(seriesTitle.CleanSeriesTitle());

            if (candidates == null)
            {
                return null;
            }

            candidates = FilterSceneMappings(candidates, releaseTitle);

            if (candidates.Count <= 1)
            {
                return candidates;
            }

            var exactMatch = candidates.OrderByDescending(v => v.SeasonNumber)
                                       .Where(v => v.Title == seriesTitle)
                                       .ToList();

            if (exactMatch.Any())
            {
                return exactMatch;
            }

            var closestMatch = candidates.OrderBy(v => seriesTitle.LevenshteinDistance(v.Title, 10, 1, 10))
                                         .ThenByDescending(v => v.SeasonNumber)
                                         .First();

            return candidates.Where(v => v.Title == closestMatch.Title).ToList();
        }

        private async Task RefreshCache()
        {
            var mappings = (await _repository.All()).ToList();

            _getTvdbIdCache.Update(mappings.GroupBy(v => v.ParseTerm).ToDictionary(v => v.Key, v => v.ToList()));
            _findByTvdbIdCache.Update(mappings.GroupBy(v => v.TvdbId).ToDictionary(v => v.Key.ToString(), v => v.ToList()));
        }

        private List<SceneMapping> FilterSceneMappings(List<SceneMapping> candidates, string releaseTitle)
        {
            var filteredCandidates = candidates.Where(v => v.FilterRegex.IsNotNullOrWhiteSpace()).ToList();
            var normalCandidates = candidates.Except(filteredCandidates).ToList();

            if (releaseTitle.IsNullOrWhiteSpace())
            {
                return normalCandidates;
            }

            var simpleTitle = Parser.Parser.SimplifyTitle(releaseTitle);

            filteredCandidates = filteredCandidates.Where(v => Regex.IsMatch(simpleTitle, v.FilterRegex)).ToList();

            if (filteredCandidates.Any())
            {
                return filteredCandidates;
            }

            return normalCandidates;
        }

        private List<SceneMapping> FilterSceneMappings(List<SceneMapping> candidates, int sceneSeasonNumber)
        {
            var filteredCandidates = candidates.Where(v => (v.SceneSeasonNumber ?? -1) != -1 && (v.SeasonNumber ?? -1) != -1).ToList();
            var normalCandidates = candidates.Except(filteredCandidates).ToList();

            if (sceneSeasonNumber == -1)
            {
                return normalCandidates;
            }

            if (filteredCandidates.Any())
            {
                filteredCandidates = filteredCandidates.Where(v => v.SceneSeasonNumber <= sceneSeasonNumber)
                                                       .GroupBy(v => v.Title)
                                                       .Select(d => d.OrderByDescending(v => v.SceneSeasonNumber)
                                                                     .ThenByDescending(v => v.SeasonNumber)
                                                                     .First())
                                                       .ToList();

                return filteredCandidates;
            }

            return normalCandidates;
        }

        // NOTE: IHandle<TEvent> is a shared eventing interface (50+ implementers app-wide); its
        // `void Handle(TEvent message)` signature is out of scope to convert (see architectural
        // note in ProviderFactory.cs). Blocking here via GetAwaiter().GetResult() is the
        // documented boundary.
        public void Handle(SeriesRefreshStartingEvent message)
        {
            if (message.ManualTrigger && (_findByTvdbIdCache.IsExpired(TimeSpan.FromMinutes(1)) || !_updatedAfterStartup))
            {
                UpdateMappings().GetAwaiter().GetResult();
            }
        }

        public void Handle(SeriesAddedEvent message)
        {
            if (!_updatedAfterStartup)
            {
                UpdateMappings().GetAwaiter().GetResult();
            }
        }

        public void Handle(SeriesImportedEvent message)
        {
            if (!_updatedAfterStartup)
            {
                UpdateMappings().GetAwaiter().GetResult();
            }
        }

        // NOTE: IExecute<TCommand> is the shared command-execution interface (31 implementers);
        // its `void Execute(TCommand message)` signature is out of scope to convert. Commands are
        // already run off the request thread by the command queue's background processing loop,
        // so blocking here via GetAwaiter().GetResult() does not risk a sync-context deadlock.
        public void Execute(UpdateSceneMappingCommand message)
        {
            UpdateMappings().GetAwaiter().GetResult();
        }
    }
}
