using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    /// <summary>
    /// Generic base mirroring the real ExtraFileRepository&lt;TExtraFile&gt; (shared by
    /// SubtitleFile/MetadataFile/OtherExtraFile) - concrete subclasses only need to supply the
    /// per-entity storage delegates from SpacetimeBasicRepository, this class provides the
    /// IExtraFileRepository&lt;TExtraFile&gt; surface on top.
    /// </summary>
    public abstract class SpacetimeExtraFileRepository<TExtraFile, TStdbRow> : SpacetimeBasicRepository<TExtraFile, TStdbRow>, IExtraFileRepository<TExtraFile>
        where TExtraFile : ExtraFile, new()
        where TStdbRow : class, SpacetimeDB.BSATN.IStructuralReadWrite, new()
    {
        protected SpacetimeExtraFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        public async Task DeleteForSeriesIds(List<int> seriesIds)
        {
            foreach (var row in (await All()).Where(c => seriesIds.Contains(c.SeriesId)).ToList())
            {
                await Delete(row.Id);
            }
        }

        public async Task DeleteForSeason(int seriesId, int seasonNumber)
        {
            foreach (var row in (await All()).Where(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber).ToList())
            {
                await Delete(row.Id);
            }
        }

        public async Task DeleteForEpisodeFile(int episodeFileId)
        {
            foreach (var row in (await All()).Where(c => c.EpisodeFileId == episodeFileId).ToList())
            {
                await Delete(row.Id);
            }
        }

        public async Task<List<TExtraFile>> GetFilesBySeries(int seriesId) => (await All()).Where(c => c.SeriesId == seriesId).ToList();

        public async Task<List<TExtraFile>> GetFilesBySeason(int seriesId, int seasonNumber) =>
            (await All()).Where(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber).ToList();

        public async Task<List<TExtraFile>> GetFilesByEpisodeFile(int episodeFileId) => (await All()).Where(c => c.EpisodeFileId == episodeFileId).ToList();

        public async Task<TExtraFile> FindByPath(int seriesId, string path) =>
            (await All()).SingleOrDefault(c => c.SeriesId == seriesId && c.RelativePath == path);
    }
}
