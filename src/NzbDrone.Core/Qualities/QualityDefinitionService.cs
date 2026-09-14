using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities.Commands;

namespace NzbDrone.Core.Qualities
{
    public interface IQualityDefinitionService
    {
        void Update(QualityDefinition qualityDefinition);
        void UpdateMany(List<QualityDefinition> qualityDefinitions);
        List<QualityDefinition> All();
        QualityDefinition GetById(int id);
        QualityDefinition Get(Quality quality);
    }

    // NOTE: IQualityDefinitionService is consumed synchronously from Organizer/FileNameBuilder.cs
    // (another agent's active domain this round) on a hot per-file naming path. Rather than
    // making this public API async - which would force an unrelated conversion of that call
    // graph as a side effect of this file - we keep the interface synchronous and bridge to the
    // now-async IQualityDefinitionRepository via GetAwaiter().GetResult(), same as the documented
    // IHandle<TEvent> boundary elsewhere: this runs under ASP.NET Core, which carries no
    // SynchronizationContext, so it cannot deadlock. This mirrors the deliberate ConfigService
    // carve-out (a conversion whose blast radius warrants a separate follow-up task).
    public class QualityDefinitionService : IQualityDefinitionService, IExecute<ResetQualityDefinitionsCommand>, IHandle<ApplicationStartedEvent>
    {
        private readonly IQualityDefinitionRepository _repo;
        private readonly ICached<Dictionary<Quality, QualityDefinition>> _cache;
        private readonly Logger _logger;

        public QualityDefinitionService(IQualityDefinitionRepository repo, ICacheManager cacheManager, Logger logger)
        {
            _repo = repo;
            _cache = cacheManager.GetCache<Dictionary<Quality, QualityDefinition>>(this.GetType());
            _logger = logger;
        }

        private Dictionary<Quality, QualityDefinition> GetAll()
        {
            return _cache.Get("all", () => _repo.All().GetAwaiter().GetResult().Select(WithWeight).ToDictionary(v => v.Quality), TimeSpan.FromSeconds(5.0));
        }

        public void Update(QualityDefinition qualityDefinition)
        {
            _repo.Update(qualityDefinition).GetAwaiter().GetResult();

            _cache.Clear();
        }

        public void UpdateMany(List<QualityDefinition> qualityDefinitions)
        {
            _repo.UpdateMany(qualityDefinitions).GetAwaiter().GetResult();
            _cache.Clear();
        }

        public List<QualityDefinition> All()
        {
            return GetAll().Values.OrderBy(d => d.Weight).ToList();
        }

        public QualityDefinition GetById(int id)
        {
            return GetAll().Values.Single(v => v.Id == id);
        }

        public QualityDefinition Get(Quality quality)
        {
            return GetAll()[quality];
        }

        private void InsertMissingDefinitions()
        {
            var insertList = new List<QualityDefinition>();
            var updateList = new List<QualityDefinition>();

            var allDefinitions = Quality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ToList();
            var existingDefinitions = _repo.All().GetAwaiter().GetResult().ToList();

            foreach (var definition in allDefinitions)
            {
                var existing = existingDefinitions.SingleOrDefault(d => d.Quality == definition.Quality);

                if (existing == null)
                {
                    insertList.Add(definition);
                }
                else
                {
                    updateList.Add(existing);
                    existingDefinitions.Remove(existing);
                }
            }

            _repo.InsertMany(insertList).GetAwaiter().GetResult();
            _repo.UpdateMany(updateList).GetAwaiter().GetResult();
            _repo.DeleteMany(existingDefinitions).GetAwaiter().GetResult();

            _cache.Clear();
        }

        private static QualityDefinition WithWeight(QualityDefinition definition)
        {
            definition.Weight = Quality.DefaultQualityDefinitions.Single(d => d.Quality == definition.Quality).Weight;

            return definition;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _logger.Debug("Setting up default quality config");

            InsertMissingDefinitions();
        }

        public void Execute(ResetQualityDefinitionsCommand message)
        {
            var updateList = new List<QualityDefinition>();

            var allDefinitions = Quality.DefaultQualityDefinitions.OrderBy(d => d.Weight).ToList();
            var existingDefinitions = _repo.All().GetAwaiter().GetResult().ToList();

            foreach (var definition in allDefinitions)
            {
                var existing = existingDefinitions.SingleOrDefault(d => d.Quality == definition.Quality);

                existing.Title = message.ResetTitles ? definition.Title : existing.Title;

                updateList.Add(existing);
            }

            _repo.UpdateMany(updateList).GetAwaiter().GetResult();

            _cache.Clear();
        }
    }
}
