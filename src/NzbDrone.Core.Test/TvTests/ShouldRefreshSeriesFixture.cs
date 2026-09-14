using System;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class ShouldRefreshSeriesFixture : TestBase<ShouldRefreshSeries>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(v => v.Status = SeriesStatusType.Continuing)
                                     .Build();

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(_series.Id))
                  .ReturnsAsync(Builder<Episode>.CreateListOfSize(2)
                                           .All()
                                           .With(e => e.AirDateUtc = DateTime.Today.AddDays(-100))
                                           .Build()
                                           .ToList());
        }

        private void GivenSeriesIsEnded()
        {
            _series.Status = SeriesStatusType.Ended;
        }

        private void GivenSeriesIsDeleted()
        {
            _series.Status = SeriesStatusType.Deleted;
        }

        private void GivenSeriesIsUpcoming()
        {
            _series.Status = SeriesStatusType.Upcoming;
        }

        private void GivenSeriesLastRefreshedMonthsAgo()
        {
            _series.LastInfoSync = DateTime.UtcNow.AddDays(-90);
        }

        private void GivenSeriesLastRefreshedYesterday()
        {
            _series.LastInfoSync = DateTime.UtcNow.AddDays(-1);
        }

        private void GivenSeriesLastRefreshedHalfADayAgo()
        {
            _series.LastInfoSync = DateTime.UtcNow.AddHours(-12);
        }

        private void GivenSeriesLastRefreshedRecently()
        {
            _series.LastInfoSync = DateTime.UtcNow.AddMinutes(-30);
        }

        private void GivenRecentlyAired()
        {
            Mocker.GetMock<IEpisodeService>()
                              .Setup(s => s.GetEpisodeBySeries(_series.Id))
                              .ReturnsAsync(Builder<Episode>.CreateListOfSize(2)
                                                       .TheFirst(1)
                                                       .With(e => e.AirDateUtc = DateTime.Today.AddDays(-7))
                                                       .TheLast(1)
                                                       .With(e => e.AirDateUtc = DateTime.Today.AddDays(-100))
                                                       .Build()
                                                       .ToList());
        }

        [Test]
        public async Task should_return_true_if_running_series_last_refreshed_more_than_6_hours_ago()
        {
            GivenSeriesLastRefreshedHalfADayAgo();

            (await Subject.ShouldRefresh(_series)).Should().BeTrue();
        }

        [Test]
        public async Task should_return_false_if_running_series_last_refreshed_less_than_6_hours_ago()
        {
            GivenSeriesLastRefreshedRecently();

            (await Subject.ShouldRefresh(_series)).Should().BeFalse();
        }

        [Test]
        public async Task should_return_false_if_ended_series_last_refreshed_yesterday()
        {
            GivenSeriesIsEnded();
            GivenSeriesLastRefreshedYesterday();

            (await Subject.ShouldRefresh(_series)).Should().BeFalse();
        }

        [Test]
        public async Task should_return_true_if_series_last_refreshed_more_than_30_days_ago()
        {
            GivenSeriesIsEnded();
            GivenSeriesLastRefreshedMonthsAgo();

            (await Subject.ShouldRefresh(_series)).Should().BeTrue();
        }

        [Test]
        public async Task should_return_true_if_episode_aired_in_last_30_days()
        {
            GivenSeriesIsEnded();
            GivenSeriesLastRefreshedYesterday();

            GivenRecentlyAired();

            (await Subject.ShouldRefresh(_series)).Should().BeTrue();
        }

        [Test]
        public async Task should_return_false_when_recently_refreshed_ended_show_has_not_aired_for_30_days()
        {
            GivenSeriesIsEnded();
            GivenSeriesLastRefreshedYesterday();

            (await Subject.ShouldRefresh(_series)).Should().BeFalse();
        }

        [Test]
        public async Task should_return_false_when_recently_refreshed_ended_show_aired_in_last_30_days()
        {
            GivenSeriesIsEnded();
            GivenSeriesLastRefreshedRecently();

            GivenRecentlyAired();

            (await Subject.ShouldRefresh(_series)).Should().BeFalse();
        }

        [Test]
        public async Task should_return_true_if_deleted_series_last_refreshed_more_than_6_hours_ago()
        {
            GivenSeriesLastRefreshedHalfADayAgo();
            GivenSeriesIsDeleted();

            (await Subject.ShouldRefresh(_series)).Should().BeTrue();
        }

        [Test]
        public async Task should_return_true_if_upcoming_series_last_refreshed_more_than_6_hours_ago()
        {
            GivenSeriesLastRefreshedHalfADayAgo();
            GivenSeriesIsUpcoming();

            (await Subject.ShouldRefresh(_series)).Should().BeTrue();
        }
    }
}
