using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace NzbDrone.Core.Datastore
{
    public interface IBasicRepository<TModel>
        where TModel : ModelBase, new()
    {
        Task<IEnumerable<TModel>> All();
        Task<int> Count();
        Task<TModel> Find(int id);
        Task<TModel> Get(int id);
        Task<TModel> Insert(TModel model);
        Task<TModel> Update(TModel model);
        Task<TModel> Upsert(TModel model);
        Task SetFields(TModel model, params Expression<Func<TModel, object>>[] properties);
        Task Delete(TModel model);
        Task Delete(int id);
        Task<IEnumerable<TModel>> Get(IEnumerable<int> ids);
        Task InsertMany(IList<TModel> model);
        Task UpdateMany(IList<TModel> model);
        Task SetFields(IList<TModel> models, params Expression<Func<TModel, object>>[] properties);
        Task DeleteMany(List<TModel> model);
        Task DeleteMany(IEnumerable<int> ids);
        Task Purge(bool vacuum = false);
        Task<bool> HasItems();
        Task<TModel> Single();
        Task<TModel> SingleOrDefault();
        Task<PagingSpec<TModel>> GetPaged(PagingSpec<TModel> pagingSpec);
    }
}
