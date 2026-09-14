using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.ImportLists.Exclusions
{
    public interface IImportListExclusionService
    {
        Task<ImportListExclusion> Add(ImportListExclusion importListExclusion);
        Task<List<ImportListExclusion>> All();
        Task<PagingSpec<ImportListExclusion>> Paged(PagingSpec<ImportListExclusion> pagingSpec);
        Task Delete(int id);
        Task Delete(List<int> ids);
        Task<ImportListExclusion> Get(int id);
        Task<ImportListExclusion> FindByTvdbId(int tvdbId);
        Task<ImportListExclusion> Update(ImportListExclusion importListExclusion);
    }

    public class ImportListExclusionService : IImportListExclusionService, IHandleAsync<SeriesDeletedEvent>
    {
        private readonly IImportListExclusionRepository _repo;

        public ImportListExclusionService(IImportListExclusionRepository repo)
        {
            _repo = repo;
        }

        public Task<ImportListExclusion> Add(ImportListExclusion importListExclusion)
        {
            return _repo.Insert(importListExclusion);
        }

        public Task<ImportListExclusion> Update(ImportListExclusion importListExclusion)
        {
            return _repo.Update(importListExclusion);
        }

        public Task Delete(int id)
        {
            return _repo.Delete(id);
        }

        public Task Delete(List<int> ids)
        {
            return _repo.DeleteMany(ids);
        }

        public Task<ImportListExclusion> Get(int id)
        {
            return _repo.Get(id);
        }

        public Task<ImportListExclusion> FindByTvdbId(int tvdbId)
        {
            return _repo.FindByTvdbId(tvdbId);
        }

        public async Task<List<ImportListExclusion>> All()
        {
            return (await _repo.All()).ToList();
        }

        public Task<PagingSpec<ImportListExclusion>> Paged(PagingSpec<ImportListExclusion> pagingSpec)
        {
            return _repo.GetPaged(pagingSpec);
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            if (!message.AddImportListExclusion)
            {
                return;
            }

            var exclusionsToAdd = new List<ImportListExclusion>();

            foreach (var series in message.Series.DistinctBy(s => s.TvdbId))
            {
                // IHandleAsync<TEvent>.HandleAsync is a shared app-wide eventing interface we must not
                // change (void return, despite the name) - bridging is safe here: runs off the request
                // thread, no SynchronizationContext to deadlock against.
                var existingExclusion = _repo.FindByTvdbId(series.TvdbId).GetAwaiter().GetResult();

                if (existingExclusion != null)
                {
                    continue;
                }

                exclusionsToAdd.Add(new ImportListExclusion
                {
                    TvdbId = series.TvdbId,
                    Title = series.Title
                });
            }

            _repo.InsertMany(exclusionsToAdd).GetAwaiter().GetResult();
        }
    }
}
