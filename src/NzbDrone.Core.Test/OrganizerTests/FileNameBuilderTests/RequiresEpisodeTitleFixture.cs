using System.Collections.Generic;
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
    public class RequiresEpisodeTitleFixture : CoreTest<FileNameBuilder>
    {
        private Series _series;
        private Episode _episode;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>
                    .CreateNew()
                    .With(s => s.Title = "South Park")
                    .Build();

            _episode = Builder<Episode>.CreateNew()
                            .With(e => e.Title = "City Sushi")
                            .With(e => e.SeasonNumber = 15)
                            .With(e => e.EpisodeNumber = 6)
                            .With(e => e.AbsoluteEpisodeNumber = 100)
                            .Build();

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameEpisodes = true;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).ReturnsAsync(_namingConfig);
        }

        [Test]
        public async Task should_return_false_when_episode_title_is_not_part_of_the_pattern()
        {
            _namingConfig.StandardEpisodeFormat = "{Series Title} S{season:00}E{episode:00}";
            (await Subject.RequiresEpisodeTitle(_series, new List<Episode> { _episode })).Should().BeFalse();
        }

        [Test]
        public async Task should_return_false_if_renaming_episodes_is_off()
        {
            _namingConfig.RenameEpisodes = false;
            (await Subject.RequiresEpisodeTitle(_series, new List<Episode> { _episode })).Should().BeFalse();
        }

        [Test]
        public async Task should_return_true_when_episode_title_is_part_of_the_pattern()
        {
            (await Subject.RequiresEpisodeTitle(_series, new List<Episode> { _episode })).Should().BeTrue();
        }
    }
}
