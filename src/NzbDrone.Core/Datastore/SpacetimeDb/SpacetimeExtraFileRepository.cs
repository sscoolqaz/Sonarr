using System.Collections.Generic;
using System.Linq;
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
    {
        protected SpacetimeExtraFileRepository(ISpacetimeDbConnection connection, IEventAggregator eventAggregator)
            : base(connection, eventAggregator)
        {
        }

        public void DeleteForSeriesIds(List<int> seriesIds)
        {
            foreach (var row in All().Where(c => seriesIds.Contains(c.SeriesId)).ToList())
            {
                Delete(row.Id);
            }
        }

        public void DeleteForSeason(int seriesId, int seasonNumber)
        {
            foreach (var row in All().Where(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber).ToList())
            {
                Delete(row.Id);
            }
        }

        public void DeleteForEpisodeFile(int episodeFileId)
        {
            foreach (var row in All().Where(c => c.EpisodeFileId == episodeFileId).ToList())
            {
                Delete(row.Id);
            }
        }

        public List<TExtraFile> GetFilesBySeries(int seriesId) => All().Where(c => c.SeriesId == seriesId).ToList();

        public List<TExtraFile> GetFilesBySeason(int seriesId, int seasonNumber) =>
            All().Where(c => c.SeriesId == seriesId && c.SeasonNumber == seasonNumber).ToList();

        public List<TExtraFile> GetFilesByEpisodeFile(int episodeFileId) => All().Where(c => c.EpisodeFileId == episodeFileId).ToList();

        public TExtraFile FindByPath(int seriesId, string path) =>
            All().SingleOrDefault(c => c.SeriesId == seriesId && c.RelativePath == path);
    }
}
