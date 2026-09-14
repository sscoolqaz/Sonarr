using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    [TestFixture]
    public class SeriesTitleFirstCharacterFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>
                    .CreateNew()
                    .Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameEpisodes = true;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).ReturnsAsync(_namingConfig);

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));
        }

        [TestCase("The Mist", "M\\The Mist")]
        [TestCase("A", "A\\A")]
        [TestCase("30 Rock", "3\\30 Rock")]
        [TestCase("The '80s Greatest", "8\\The '80s Greatest")]
        [TestCase("좀비버스", "좀\\좀비버스")]
        [TestCase("¡Mucha Lucha!", "M\\¡Mucha Lucha!")]
        [TestCase(".hack", "H\\hack")]
        [TestCase("Ütopya", "U\\Ütopya")]
        [TestCase("Æon Flux", "A\\Æon Flux")]

        public async Task should_get_expected_folder_name_back(string title, string expected)
        {
            _series.Title = title;
            _namingConfig.SeriesFolderFormat = "{Series TitleFirstCharacter}\\{Series Title}";

            (await Subject.GetSeriesFolder(_series)).Should().Be(expected);
        }

        [Test]
        public async Task should_be_able_to_use_lower_case_first_character()
        {
            _series.Title = "Westworld";
            _namingConfig.SeriesFolderFormat = "{series titlefirstcharacter}\\{series title}";

            (await Subject.GetSeriesFolder(_series)).Should().Be("w\\westworld");
        }
    }
}
