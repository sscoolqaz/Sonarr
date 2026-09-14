using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NzbDrone.Core.CustomFilters
{
    public interface ICustomFilterService
    {
        Task<CustomFilter> Add(CustomFilter customFilter);
        Task<List<CustomFilter>> All();
        Task Delete(int id);
        Task<CustomFilter> Get(int id);
        Task<CustomFilter> Update(CustomFilter customFilter);
    }

    public class CustomFilterService : ICustomFilterService
    {
        private readonly ICustomFilterRepository _repo;

        public CustomFilterService(ICustomFilterRepository repo)
        {
            _repo = repo;
        }

        public Task<CustomFilter> Add(CustomFilter customFilter)
        {
            return _repo.Insert(customFilter);
        }

        public Task<CustomFilter> Update(CustomFilter customFilter)
        {
            return _repo.Update(customFilter);
        }

        public Task Delete(int id)
        {
            return _repo.Delete(id);
        }

        public Task<CustomFilter> Get(int id)
        {
            return _repo.Get(id);
        }

        public async Task<List<CustomFilter>> All()
        {
            return (await _repo.All()).ToList();
        }
    }
}
