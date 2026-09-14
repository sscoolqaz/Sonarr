using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Cache;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.CustomFormats
{
    public interface ICustomFormatService
    {
        Task Update(CustomFormat customFormat);
        Task Update(List<CustomFormat> customFormat);
        Task<CustomFormat> Insert(CustomFormat customFormat);
        Task<List<CustomFormat>> All();
        Task<CustomFormat> GetById(int id);
        Task Delete(int id);
        Task Delete(List<int> ids);
    }

    public class CustomFormatService : ICustomFormatService
    {
        private readonly ICustomFormatRepository _formatRepository;
        private readonly IEventAggregator _eventAggregator;
        private readonly ICached<Dictionary<int, CustomFormat>> _cache;

        public CustomFormatService(ICustomFormatRepository formatRepository,
                                   ICacheManager cacheManager,
                                   IEventAggregator eventAggregator)
        {
            _formatRepository = formatRepository;
            _eventAggregator = eventAggregator;
            _cache = cacheManager.GetCache<Dictionary<int, CustomFormat>>(typeof(CustomFormat), "formats");
        }

        private async Task<Dictionary<int, CustomFormat>> AllDictionary()
        {
            var cached = _cache.Find("all");

            if (cached != null)
            {
                return cached;
            }

            var all = (await _formatRepository.All()).ToDictionary(m => m.Id);
            _cache.Set("all", all);

            return all;
        }

        public async Task<List<CustomFormat>> All()
        {
            return (await AllDictionary()).Values.ToList();
        }

        public async Task<CustomFormat> GetById(int id)
        {
            return (await AllDictionary())[id];
        }

        public async Task Update(CustomFormat customFormat)
        {
            await _formatRepository.Update(customFormat);
            _cache.Clear();
        }

        public async Task Update(List<CustomFormat> customFormat)
        {
            await _formatRepository.UpdateMany(customFormat);
            _cache.Clear();
        }

        public async Task<CustomFormat> Insert(CustomFormat customFormat)
        {
            // Add to DB then insert into profiles
            var result = await _formatRepository.Insert(customFormat);
            _cache.Clear();

            _eventAggregator.PublishEvent(new CustomFormatAddedEvent(result));

            return result;
        }

        public async Task Delete(int id)
        {
            var format = await _formatRepository.Get(id);

            // Remove from profiles before removing from DB
            _eventAggregator.PublishEvent(new CustomFormatDeletedEvent(format));

            await _formatRepository.Delete(id);
            _cache.Clear();
        }

        public async Task Delete(List<int> ids)
        {
            foreach (var id in ids)
            {
                var format = await _formatRepository.Get(id);

                // Remove from profiles before removing from DB
                _eventAggregator.PublishEvent(new CustomFormatDeletedEvent(format));

                await _formatRepository.Delete(id);
            }

            _cache.Clear();
        }
    }
}
