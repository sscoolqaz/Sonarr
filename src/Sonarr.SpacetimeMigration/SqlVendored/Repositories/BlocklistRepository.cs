using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Blocklisting
{
    public class BlocklistRepository : BasicRepository<Blocklist>, IBlocklistRepository
    {
        public BlocklistRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<List<Blocklist>> BlocklistedByTitle(int seriesId, string sourceTitle)
        {
            return Task.FromResult(Query(e => e.SeriesId == seriesId && e.SourceTitle.Contains(sourceTitle)));
        }

        public Task<List<Blocklist>> BlocklistedByTorrentInfoHash(int seriesId, string torrentInfoHash)
        {
            return Task.FromResult(Query(e => e.SeriesId == seriesId && e.TorrentInfoHash.Contains(torrentInfoHash)));
        }

        public Task<List<Blocklist>> BlocklistedBySeries(int seriesId)
        {
            return Task.FromResult(Query(b => b.SeriesId == seriesId));
        }

        public Task DeleteForSeriesIds(List<int> seriesIds)
        {
            Delete(x => seriesIds.Contains(x.SeriesId));
            return Task.CompletedTask;
        }

        public override Task<PagingSpec<Blocklist>> GetPaged(PagingSpec<Blocklist> pagingSpec)
        {
            var sortingByQuality = string.Equals(pagingSpec.SortKey, "quality", StringComparison.OrdinalIgnoreCase);
            var customSortExpression = sortingByQuality ? "COALESCE(\"r\".\"Score\", -1)" : null;

            pagingSpec.Records = GetPagedRecords(PagedBuilder(sortingByQuality), pagingSpec, PagedQuery, customSortExpression);

            var countTemplate = $"SELECT COUNT(*) FROM (SELECT /**select**/ FROM \"{TableMapping.Mapper.TableNameMapping(typeof(Blocklist))}\" /**join**/ /**innerjoin**/ /**leftjoin**/ /**where**/ /**groupby**/ /**having**/) AS \"Inner\"";
            pagingSpec.TotalRecords = GetPagedRecordCount(PagedBuilder(sortingByQuality).Select(typeof(Blocklist)), pagingSpec, countTemplate);

            return Task.FromResult(pagingSpec);
        }

        protected override SqlBuilder PagedBuilder() => PagedBuilder(false);

        private SqlBuilder PagedBuilder(bool joinQualityRanks)
        {
            var builder = Builder()
                .Join<Blocklist, Series>((b, m) => b.SeriesId == m.Id);

            if (joinQualityRanks)
            {
                var qualityIdExpr = _database.DatabaseType == DatabaseType.PostgreSQL
                    ? "(\"Blocklist\".\"Quality\"::jsonb ->> 'quality')::int"
                    : "json_extract(\"Blocklist\".\"Quality\", '$.quality')";

                builder.LeftJoin(
                    $"\"QualityProfileQualityRanks\" AS \"r\" " +
                    $"ON \"r\".\"ProfileId\" = \"Series\".\"QualityProfileId\" " +
                    $"AND \"r\".\"QualityId\" = {qualityIdExpr}");
            }

            return builder;
        }

        protected override IEnumerable<Blocklist> PagedQuery(SqlBuilder builder) =>
            _database.QueryJoined<Blocklist, Series>(builder, (blocklist, series) =>
            {
                blocklist.Series = series;
                return blocklist;
            });
    }
}
