using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tags
{
    public class TagRepository : BasicRepository<Tag>, ITagRepository
    {
        public TagRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<Tag> GetByLabel(string label)
        {
            var model = Query(c => c.Label == label).SingleOrDefault();

            if (model == null)
            {
                throw new InvalidOperationException("Didn't find tag with label " + label);
            }

            return Task.FromResult(model);
        }

        public Task<Tag> FindByLabel(string label)
        {
            return Task.FromResult(Query(c => c.Label == label).SingleOrDefault());
        }

        public Task<List<Tag>> GetTags(HashSet<int> tagIds)
        {
            return Task.FromResult(Query(t => tagIds.Contains(t.Id)));
        }
    }
}
