using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.AutoTagging.Specifications;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.AutoTagging
{
    [TestFixture]
    public class AutoTaggingServiceFixture : CoreTest<AutoTaggingService>
    {
        private Series _series;
        private AutoTag _tag;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Genres = new List<string> { "Comedy" })
                                     .Build();

            _tag = new AutoTag
                           {
                               Name = "Test",
                               Specifications = new List<IAutoTaggingSpecification>
                                                {
                                                    new GenreSpecification
                                                    {
                                                        Name = "Genre",
                                                        Value = new List<string>
                                                                {
                                                                    "Comedy"
                                                                }
                                                    }
                                                },
                               Tags = new HashSet<int> { 1 },
                               RemoveTagsAutomatically = false
                           };
        }

        private void GivenAutoTags(List<AutoTag> autoTags)
        {
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(s => s.All())
                  .ReturnsAsync(autoTags);
        }

        [Test]
        public async Task should_not_have_changes_if_there_are_no_auto_tags()
        {
            GivenAutoTags(new List<AutoTag>());

            var result = await Subject.GetTagChanges(_series);

            result.TagsToAdd.Should().BeEmpty();
            result.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public async Task should_have_tags_to_add_if_series_does_not_have_match_tag()
        {
            GivenAutoTags(new List<AutoTag> { _tag });

            var result = await Subject.GetTagChanges(_series);

            result.TagsToAdd.Should().HaveCount(1);
            result.TagsToAdd.Should().Contain(1);
            result.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public async Task should_not_have_tags_to_remove_if_series_has_matching_tag_but_remove_is_false()
        {
            _series.Tags = new HashSet<int> { 1 };
            _series.Genres = new List<string> { "NotComedy" };

            GivenAutoTags(new List<AutoTag> { _tag });

            var result = await Subject.GetTagChanges(_series);

            result.TagsToAdd.Should().BeEmpty();
            result.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public async Task should_have_tags_to_remove_if_series_has_matching_tag_and_remove_is_true()
        {
            _series.Tags = new HashSet<int> { 1 };
            _series.Genres = new List<string> { "NotComedy" };

            _tag.RemoveTagsAutomatically = true;

            GivenAutoTags(new List<AutoTag> { _tag });

            var result = await Subject.GetTagChanges(_series);

            result.TagsToAdd.Should().BeEmpty();
            result.TagsToRemove.Should().HaveCount(1);
            result.TagsToRemove.Should().Contain(1);
        }

        [Test]
        public async Task should_have_tags_to_add_if_series_does_not_have_match_tag_and_series_matches_all_rules()
        {
            _tag.Specifications.Add(new SeriesTypeSpecification
                                    {
                                        Name = "Series Type",
                                        Value = (int)_series.SeriesType
                                    });

            GivenAutoTags(new List<AutoTag> { _tag });

            var result = await Subject.GetTagChanges(_series);

            result.TagsToAdd.Should().HaveCount(1);
            result.TagsToAdd.Should().Contain(1);
            result.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public async Task should_match_if_specification_is_negated()
        {
            _series.Genres = new List<string> { "NotComedy" };

            _tag.Specifications.First().Negate = true;

            GivenAutoTags(new List<AutoTag> { _tag });

            var result = await Subject.GetTagChanges(_series);

            result.TagsToAdd.Should().HaveCount(1);
            result.TagsToAdd.Should().Contain(1);
            result.TagsToRemove.Should().BeEmpty();
        }

        [Test]
        public void insert_should_propagate_repository_exception_and_not_publish_an_update_event()
        {
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.Insert(It.IsAny<AutoTag>()))
                  .ThrowsAsync(new InvalidOperationException("insert failed"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Insert(_tag));

            // AutoTagsUpdatedEvent (and the cache clear before it) only belong after a
            // successful write - if the awaited repository call's exception were ever
            // swallowed, this would still fire despite nothing having been persisted.
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Never);
        }

        [Test]
        public void update_should_propagate_repository_exception_and_not_publish_an_update_event()
        {
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.Update(It.IsAny<AutoTag>()))
                  .ThrowsAsync(new InvalidOperationException("update failed"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Update(_tag));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Never);
        }

        [Test]
        public void delete_should_propagate_repository_exception_and_not_publish_an_update_event()
        {
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.Delete(It.IsAny<int>()))
                  .ThrowsAsync(new InvalidOperationException("delete failed"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Delete(1));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<AutoTagsUpdatedEvent>()), Times.Never);
        }

        [Test]
        public void get_tag_changes_should_propagate_repository_exception()
        {
            Mocker.GetMock<IAutoTaggingRepository>()
                  .Setup(r => r.All())
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.GetTagChanges(_series));
        }
    }
}
