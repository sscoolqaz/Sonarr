using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class IdFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>
                      .CreateNew()
                      .With(s => s.Title = "Series Title")
                      .With(s => s.ImdbId = "tt12345")
                      .With(s => s.TvdbId = 12345)
                      .With(s => s.TvRageId = 54321)
                      .Build();

            _namingConfig = NamingConfig.Default;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).ReturnsAsync(_namingConfig);
        }

        [Test]
        public async Task should_add_imdb_id()
        {
            _namingConfig.SeriesFolderFormat = "{Series Title} ({ImdbId})";

            (await Subject.GetSeriesFolder(_series))
                   .Should().Be($"Series Title ({_series.ImdbId})");
        }

        [Test]
        public async Task should_add_tvdb_id()
        {
            _namingConfig.SeriesFolderFormat = "{Series Title} ({TvdbId})";

            (await Subject.GetSeriesFolder(_series))
                   .Should().Be($"Series Title ({_series.TvdbId})");
        }

        [Test]
        public async Task should_add_tvmaze_id()
        {
            _namingConfig.SeriesFolderFormat = "{Series Title} ({TvMazeId})";

            (await Subject.GetSeriesFolder(_series))
                   .Should().Be($"Series Title ({_series.TvMazeId})");
        }

        [Test]
        public async Task should_add_tmdb_id()
        {
            _namingConfig.SeriesFolderFormat = "{Series Title} ({TmdbId})";

            (await Subject.GetSeriesFolder(_series))
                .Should().Be($"Series Title ({_series.TmdbId})");
        }
    }
}
