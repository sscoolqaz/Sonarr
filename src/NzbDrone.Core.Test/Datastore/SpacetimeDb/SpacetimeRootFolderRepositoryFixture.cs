using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore.SpacetimeDb;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.Datastore.SpacetimeDb
{
    /// <summary>
    /// Phase 4 proof for the generic SpacetimeBasicRepository base: exercises the real
    /// RootFolderService business-logic layer against a SpacetimeRootFolderRepository backed by
    /// a live SpacetimeDB server. See SpacetimeTagRepositoryFixture's remarks for the server
    /// setup this requires.
    /// </summary>
    [TestFixture]
    [Explicit("Requires a live SpacetimeDB server - see SpacetimeTagRepositoryFixture remarks")]
    public class SpacetimeRootFolderRepositoryFixture : CoreTest<RootFolderService>
    {
        private ISpacetimeDbConnection _connection;
        private SpacetimeRootFolderRepository _repo;

        [SetUp]
        public void Setup()
        {
            _connection = new SpacetimeDbConnection("http://127.0.0.1:3000", "sonarr-spacetime-dev");
            _repo = new SpacetimeRootFolderRepository(_connection, Mocker.GetMock<IEventAggregator>().Object);
            Mocker.SetConstant<IRootFolderRepository>(_repo);

            Mocker.GetMock<ISeriesRepository>().Setup(s => s.AllSeriesPaths()).Returns(new Dictionary<int, string>());
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(NamingConfig.Default);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderExists(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderWritable(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FolderEmpty(It.IsAny<string>())).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetAvailableSpace(It.IsAny<string>())).Returns(0);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetTotalSize(It.IsAny<string>())).Returns(0);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetDirectories(It.IsAny<string>())).Returns(Array.Empty<string>());
        }

        [TearDown]
        public void TearDown()
        {
            (_connection as IDisposable)?.Dispose();
        }

        [Test]
        public void should_add_get_remove_root_folder_through_real_service()
        {
            var path = $"/tmp/spike-root-{Guid.NewGuid():N}".Substring(0, 30);

            var added = Subject.Add(new RootFolder { Path = path });
            added.Id.Should().BeGreaterThan(0);

            Subject.All().Should().Contain(r => r.Id == added.Id && r.Path == path);

            var fetched = Subject.Get(added.Id, timeout: true);
            fetched.Path.Should().Be(path);

            Subject.Remove(added.Id);
            Subject.All().Should().NotContain(r => r.Id == added.Id);
        }
    }
}
