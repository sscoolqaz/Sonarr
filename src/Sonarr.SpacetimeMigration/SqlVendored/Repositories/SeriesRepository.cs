using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Tv
{
    public class SeriesRepository : BasicRepository<Series>, ISeriesRepository
    {
        public SeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public Task<bool> SeriesPathExists(string path)
        {
            return Task.FromResult(Query(c => c.Path == path).Any());
        }

        public Task<Series> FindByTitle(string cleanTitle)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            var series = Query(s => s.CleanTitle == cleanTitle)
                                        .ToList();

            return Task.FromResult(ReturnSingleSeriesOrThrow(series));
        }

        public Task<Series> FindByTitle(string cleanTitle, int year)
        {
            cleanTitle = cleanTitle.ToLowerInvariant();

            var series = Query(s => s.CleanTitle == cleanTitle && s.Year == year).ToList();

            return Task.FromResult(ReturnSingleSeriesOrThrow(series));
        }

        public Task<List<Series>> FindByTitleInexact(string cleanTitle)
        {
            var builder = Builder().Where($"instr(@cleanTitle, \"Series\".\"CleanTitle\")", new { cleanTitle = cleanTitle });

            if (_database.DatabaseType == DatabaseType.PostgreSQL)
            {
                builder = Builder().Where($"(strpos(@cleanTitle, \"Series\".\"CleanTitle\") > 0)", new { cleanTitle = cleanTitle });
            }

            return Task.FromResult(Query(builder).ToList());
        }

        public Task<Series> FindByTvdbId(int tvdbId)
        {
            return Task.FromResult(Query(s => s.TvdbId == tvdbId).SingleOrDefault());
        }

        public Task<Series> FindByTvRageId(int tvRageId)
        {
            return Task.FromResult(Query(s => s.TvRageId == tvRageId).SingleOrDefault());
        }

        public Task<Series> FindByImdbId(string imdbId)
        {
            return Task.FromResult(Query(s => s.ImdbId == imdbId).SingleOrDefault());
        }

        public Task<Series> FindByPath(string path)
        {
            return Task.FromResult(Query(s => s.Path == path)
                        .FirstOrDefault());
        }

        public Task<Dictionary<int, int>> AllSeriesTvdbIds()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"TvdbId\" AS Value FROM \"Series\"";
                return Task.FromResult(conn.Query<KeyValuePair<int, int>>(strSql).ToDictionary(x => x.Key, x => x.Value));
            }
        }

        public Task<Dictionary<int, string>> AllSeriesPaths()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"Path\" AS Value FROM \"Series\"";
                return Task.FromResult(conn.Query<KeyValuePair<int, string>>(strSql).ToDictionary(x => x.Key, x => x.Value));
            }
        }

        public Task<Dictionary<int, List<int>>> AllSeriesTags()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"Tags\" AS Value FROM \"Series\" WHERE \"Tags\" IS NOT NULL";
                return Task.FromResult(conn.Query<KeyValuePair<int, List<int>>>(strSql).ToDictionary(x => x.Key, x => x.Value));
            }
        }

        public Task<Dictionary<int, int>> AllSeriesQualityProfiles()
        {
            using (var conn = _database.OpenConnection())
            {
                var strSql = "SELECT \"Id\" AS Key, \"QualityProfileId\" AS Value FROM \"Series\"";
                return Task.FromResult(conn.Query<KeyValuePair<int, int>>(strSql).ToDictionary(x => x.Key, x => x.Value));
            }
        }

        private Series ReturnSingleSeriesOrThrow(List<Series> series)
        {
            if (series.Count == 0)
            {
                return null;
            }

            if (series.Count == 1)
            {
                return series.First();
            }

            throw new MultipleSeriesFoundException(series, "Expected one series, but found {0}. Matching series: {1}", series.Count, string.Join(", ", series));
        }
    }
}
