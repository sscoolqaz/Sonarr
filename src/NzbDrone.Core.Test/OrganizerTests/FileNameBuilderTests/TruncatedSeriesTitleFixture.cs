using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]

    public class TruncatedSeriesTitleFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>
                    .CreateNew()
                    .With(s => s.Title = "Series Title")
                    .Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.MultiEpisodeStyle = 0;
            _namingConfig.RenameEpisodes = true;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).ReturnsAsync(_namingConfig);

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));

            Mocker.GetMock<ICustomFormatService>()
                  .Setup(v => v.All())
                  .ReturnsAsync(new List<CustomFormat>());
        }

        [TestCase("{Series Title:16}", "The Fantastic...")]
        [TestCase("{Series TitleThe:17}", "Fantastic Life...")]
        [TestCase("{Series CleanTitle:-13}", "...Mr. Sisko")]
        public async Task should_truncate_series_title(string format, string expected)
        {
            _series.Title = "The Fantastic Life of Mr. Sisko";
            _namingConfig.SeriesFolderFormat = format;

            var result = await Subject.GetSeriesFolder(_series, _namingConfig);
            result.Should().Be(expected);
        }
    }
}
