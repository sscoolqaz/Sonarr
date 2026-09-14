using System;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Tags
{
    /// <summary>
    /// TagService previously had no mocked-repository unit coverage at all - the only existing
    /// fixture touching it, SpacetimeTagRepositoryFixture, is [Explicit] and needs a live
    /// SpacetimeDB server, so it never runs as part of the normal suite. This fixture fills that
    /// gap with fast, always-on coverage of TagService's own logic against a mocked
    /// ITagRepository, focused specifically on async error propagation: TagService is now
    /// Task-returning throughout (see ITagService), and every method here awaits a repository
    /// call directly with no try/catch of its own - an exception from the repository should
    /// surface to the caller unchanged, and any side effect that's only supposed to happen after
    /// a successful write (publishing TagsUpdatedEvent) must not fire when the write fails.
    /// </summary>
    [TestFixture]
    public class TagServiceFixture : CoreTest<TagService>
    {
        [Test]
        public void get_tag_by_id_should_propagate_repository_exception()
        {
            Mocker.GetMock<ITagRepository>()
                  .Setup(r => r.Get(It.IsAny<int>()))
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.GetTag(1));
        }

        [Test]
        public void get_tag_by_label_should_propagate_repository_exception()
        {
            Mocker.GetMock<ITagRepository>()
                  .Setup(r => r.GetByLabel(It.IsAny<string>()))
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.GetTag("newtag"));
        }

        [Test]
        public void add_should_propagate_repository_insert_exception_and_not_publish_an_update_event()
        {
            Mocker.GetMock<ITagRepository>()
                  .Setup(r => r.FindByLabel(It.IsAny<string>()))
                  .ReturnsAsync((Tag)null);

            Mocker.GetMock<ITagRepository>()
                  .Setup(r => r.Insert(It.IsAny<Tag>()))
                  .ThrowsAsync(new InvalidOperationException("insert failed"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Add(new Tag { Label = "newtag" }));

            // TagsUpdatedEvent is published immediately after the await Insert(...) call with no
            // guard - if the exception were ever accidentally swallowed (e.g. a stray
            // try/catch introduced later, or the await dropped), this would still fire even
            // though nothing was actually persisted.
            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<TagsUpdatedEvent>()), Times.Never);
        }

        [Test]
        public void update_should_propagate_repository_update_exception_and_not_publish_an_update_event()
        {
            Mocker.GetMock<ITagRepository>()
                  .Setup(r => r.Update(It.IsAny<Tag>()))
                  .ThrowsAsync(new InvalidOperationException("update failed"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Update(new Tag { Id = 1, Label = "renamed" }));

            Mocker.GetMock<IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.IsAny<TagsUpdatedEvent>()), Times.Never);
        }

        [Test]
        public void all_should_propagate_repository_exception()
        {
            Mocker.GetMock<ITagRepository>()
                  .Setup(r => r.All())
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.All());
        }
    }
}
