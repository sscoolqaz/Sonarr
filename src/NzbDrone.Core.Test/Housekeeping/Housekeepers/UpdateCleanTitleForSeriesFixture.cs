using System.Threading.Tasks;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    [TestFixture]
    public class UpdateCleanTitleForSeriesFixture : CoreTest<UpdateCleanTitleForSeries>
    {
        [Test]
        public async Task should_update_clean_title()
        {
            var series = Builder<Series>.CreateNew()
                                        .With(s => s.Title = "Full Title")
                                        .With(s => s.CleanTitle = "unclean")
                                        .Build();

            Mocker.GetMock<ISeriesRepository>()
                 .Setup(s => s.All())
                 .ReturnsAsync(new[] { series });

            await Subject.Clean();

            Mocker.GetMock<ISeriesRepository>()
                .Verify(v => v.Update(It.Is<Series>(s => s.CleanTitle == "fulltitle")), Times.Once());
        }

        [Test]
        public async Task should_not_update_unchanged_title()
        {
            var series = Builder<Series>.CreateNew()
                                        .With(s => s.Title = "Full Title")
                                        .With(s => s.CleanTitle = "fulltitle")
                                        .Build();

            Mocker.GetMock<ISeriesRepository>()
                 .Setup(s => s.All())
                 .ReturnsAsync(new[] { series });

            await Subject.Clean();

            Mocker.GetMock<ISeriesRepository>()
                .Verify(v => v.Update(It.Is<Series>(s => s.CleanTitle == "fulltitle")), Times.Never());
        }
    }
}
