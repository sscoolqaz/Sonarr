using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.RootFolderTests
{
    [TestFixture]
    public class RootFolderServiceFixture : CoreTest<RootFolderService>
    {
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderWritable(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.All())
                  .ReturnsAsync(new List<RootFolder>());

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig())
                  .ReturnsAsync(_namingConfig);
        }

        private void WithNonExistingFolder()
        {
            Mocker.GetMock<IDiskProvider>()
                .Setup(m => m.FolderExists(It.IsAny<string>()))
                .Returns(false);
        }

        [TestCase("D:\\TV Shows\\")]
        [TestCase("//server//folder")]
        public async Task should_be_able_to_add_root_dir(string path)
        {
            Mocker.GetMock<ISeriesRepository>()
                  .Setup(s => s.AllSeriesPaths())
                  .ReturnsAsync(new Dictionary<int, string>());

            var root = new RootFolder { Path = path.AsOsAgnostic() };

            await Subject.Add(root);

            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Insert(root), Times.Once());
        }

        [Test]
        public void should_throw_if_folder_being_added_doesnt_exist()
        {
            WithNonExistingFolder();

            Assert.ThrowsAsync<DirectoryNotFoundException>(async () => await Subject.Add(new RootFolder { Path = "C:\\TEST".AsOsAgnostic() }));
        }

        [Test]
        public async Task should_be_able_to_remove_root_dir()
        {
            await Subject.Remove(1);
            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Delete(1), Times.Once());
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("BAD PATH")]
        public void invalid_folder_path_throws_on_add(string path)
        {
            Assert.ThrowsAsync<ArgumentException>(async () =>
                    await Mocker.Resolve<RootFolderService>().Add(new RootFolder { Id = 0, Path = path }));
        }

        [Test]
        public void adding_duplicated_root_folder_should_throw()
        {
            Mocker.GetMock<IRootFolderRepository>().Setup(c => c.All()).ReturnsAsync(new List<RootFolder> { new RootFolder { Path = "C:\\TV".AsOsAgnostic() } });

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));
        }

        [Test]
        public void should_throw_when_adding_not_writable_folder()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(m => m.FolderWritable(It.IsAny<string>()))
                  .Returns(false);

            Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));
        }

        [TestCase("$recycle.bin")]
        [TestCase("system volume information")]
        [TestCase("recycler")]
        [TestCase("lost+found")]
        [TestCase(".appledb")]
        [TestCase(".appledesktop")]
        [TestCase(".appledouble")]
        [TestCase("@eadir")]
        [TestCase(".grab")]
        public async Task should_get_root_folder_with_subfolders_excluding_special_sub_folders(string subFolder)
        {
            var rootFolderPath = @"C:\Test\TV".AsOsAgnostic();
            var rootFolder = Builder<RootFolder>.CreateNew()
                                                .With(r => r.Path = rootFolderPath)
                                                .Build();

            var subFolders = new[]
                        {
                            "Series1",
                            "Series2",
                            "Series3",
                            subFolder
                        };

            var folders = subFolders.Select(f => Path.Combine(rootFolderPath, f)).ToArray();

            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.Get(It.IsAny<int>()))
                  .ReturnsAsync(rootFolder);

            Mocker.GetMock<ISeriesRepository>()
                  .Setup(s => s.AllSeriesPaths())
                  .ReturnsAsync(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetDirectories(rootFolder.Path))
                  .Returns(folders);

            var unmappedFolders = (await Subject.Get(rootFolder.Id, true)).UnmappedFolders;

            unmappedFolders.Count.Should().BeGreaterThan(0);
            unmappedFolders.Should().NotContain(u => u.Name == subFolder);
        }

        [Test]
        public async Task should_get_unmapped_folders_inside_letter_subfolder()
        {
            _namingConfig.SeriesFolderFormat = "{Series TitleFirstCharacter}\\{Series Title}".AsOsAgnostic();

            var rootFolderPath = @"C:\Test\TV".AsOsAgnostic();
            var rootFolder = Builder<RootFolder>.CreateNew()
                .With(r => r.Path = rootFolderPath)
                .Build();

            var subFolderPath = Path.Combine(rootFolderPath, "S");

            var subFolders = new[]
            {
                "Series1",
                "Series2",
                "Series3",
            };

            var folders = subFolders.Select(f => Path.Combine(subFolderPath, f)).ToArray();

            Mocker.GetMock<IRootFolderRepository>()
                .Setup(s => s.Get(It.IsAny<int>()))
                .ReturnsAsync(rootFolder);

            Mocker.GetMock<ISeriesRepository>()
                .Setup(s => s.AllSeriesPaths())
                .ReturnsAsync(new Dictionary<int, string>());

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(rootFolder.Path))
                .Returns(new[] { subFolderPath });

            Mocker.GetMock<IDiskProvider>()
                .Setup(s => s.GetDirectories(subFolderPath))
                .Returns(folders);

            var unmappedFolders = (await Subject.Get(rootFolder.Id, false)).UnmappedFolders;

            unmappedFolders.Count.Should().Be(3);
        }

        [Test]
        public void all_should_propagate_repository_exception_rather_than_swallow_it()
        {
            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.All())
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.All());
        }

        [Test]
        public void add_should_propagate_repository_exception_from_the_duplicate_check_without_ever_inserting()
        {
            // Add's duplicate-path check (await _rootFolderRepository.All()) runs before the
            // insert - if that repository call throws, the exception must surface as-is and
            // Insert must never be reached, not get masked by (or race) a subsequent call.
            Mocker.GetMock<IRootFolderRepository>()
                  .Setup(s => s.All())
                  .ThrowsAsync(new InvalidOperationException("repository unavailable"));

            Assert.ThrowsAsync<InvalidOperationException>(async () => await Subject.Add(new RootFolder { Path = @"C:\TV".AsOsAgnostic() }));

            Mocker.GetMock<IRootFolderRepository>().Verify(c => c.Insert(It.IsAny<RootFolder>()), Times.Never());
        }
    }
}
