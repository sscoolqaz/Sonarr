using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Download;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Notifications;
using NzbDrone.Core.Profiles.Delay;
using NzbDrone.Core.Profiles.Releases;
using NzbDrone.Core.Tags;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Datastore.SpacetimeDb
{
    /// <summary>
    /// Phase 1 spike proof: exercises the real NzbDrone.Core.Tags.TagService business-logic
    /// layer (the same layer TagController calls) against a SpacetimeTagRepository backed by
    /// a live SpacetimeDB server, instead of the normal SQLite/Postgres-backed repository.
    ///
    /// Requires a SpacetimeDB server with the Sonarr.SpacetimeModule module published as
    /// "sonarr-spacetime-dev" reachable at http://127.0.0.1:3000 (see
    /// .dev-scratch/spacetimedb-standalone for the dev podman-compose setup). Not part of the
    /// normal CI/unit test run - this is spike validation, not a regression test.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a live SpacetimeDB server - see class remarks")]
    public class SpacetimeTagRepositoryFixture : CoreTest<TagService>
    {
        private ISpacetimeDbConnection _connection;
        private SpacetimeTagRepository _repo;

        [SetUp]
        public void Setup()
        {
            _connection = new SpacetimeDbConnection("http://127.0.0.1:3000", "sonarr-spacetime-dev");
            _repo = new SpacetimeTagRepository(_connection, Mocker.GetMock<IEventAggregator>().Object);
            Mocker.SetConstant<ITagRepository>(_repo);

            // TagService.Delete() calls Details(), which fans out to every other tag-aware
            // service. None of those are under test here, so give them empty results.
            Mocker.GetMock<IDelayProfileService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<DelayProfile>());
            Mocker.GetMock<IImportListFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<ImportListDefinition>());
            Mocker.GetMock<INotificationFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<NotificationDefinition>());
            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<ReleaseProfile>());
            Mocker.GetMock<IReleaseProfileService>().Setup(s => s.AllExcludedForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<ReleaseProfile>());
            Mocker.GetMock<ISeriesService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<Series>());
            Mocker.GetMock<IIndexerFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<IndexerDefinition>());
            Mocker.GetMock<IAutoTaggingService>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<AutoTag>());
            Mocker.GetMock<IDownloadClientFactory>().Setup(s => s.AllForTag(It.IsAny<int>())).Returns(new System.Collections.Generic.List<DownloadClientDefinition>());
        }

        [TearDown]
        public void TearDown()
        {
            (_connection as IDisposable)?.Dispose();
        }

        [Test]
        public void should_add_get_update_delete_tag_through_real_tag_service()
        {
            var label = $"spike-{Guid.NewGuid():N}".Substring(0, 20);

            var added = Subject.Add(new Tag { Label = label });
            added.Id.Should().BeGreaterThan(0);
            added.Label.Should().Be(label);

            var fetchedById = Subject.GetTag(added.Id);
            fetchedById.Label.Should().Be(label);

            var fetchedByLabel = Subject.GetTag(label);
            fetchedByLabel.Id.Should().Be(added.Id);

            var updatedLabel = label + "-updated";
            Subject.Update(new Tag { Id = added.Id, Label = updatedLabel });
            Subject.GetTag(added.Id).Label.Should().Be(updatedLabel);

            Subject.All().Should().Contain(t => t.Id == added.Id && t.Label == updatedLabel);

            Subject.Delete(added.Id);
            Assert.Throws<ModelNotFoundException>(() => Subject.GetTag(added.Id));
        }

        [Test]
        public void should_return_existing_tag_when_adding_duplicate_label()
        {
            var label = $"spike-dup-{Guid.NewGuid():N}".Substring(0, 20);

            var first = Subject.Add(new Tag { Label = label });
            var second = Subject.Add(new Tag { Label = label });

            second.Id.Should().Be(first.Id);
        }
    }
}
