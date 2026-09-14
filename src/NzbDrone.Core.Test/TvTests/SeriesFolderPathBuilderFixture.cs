using System.IO;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.TvTests
{
    [TestFixture]
    public class SeriesFolderPathBuilderFixture : CoreTest<SeriesPathBuilder>
    {
        private Series _series;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                                     .With(s => s.Title = "Series Title")
                                     .With(s => s.Path = @"C:\Test\TV\Series.Title".AsOsAgnostic())
                                     .With(s => s.RootFolderPath = null)
                                     .Build();
        }

        public void GivenSeriesFolderName(string name)
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetSeriesFolder(_series, null))
                  .ReturnsAsync(name);
        }

        public void GivenExistingRootFolder(string rootFolder)
        {
            Mocker.GetMock<IRootFolderService>()
                  .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>()))
                  .ReturnsAsync(rootFolder);
        }

        [Test]
        public async Task should_create_new_series_path()
        {
            var rootFolder = @"C:\Test\TV2".AsOsAgnostic();

            GivenSeriesFolderName(_series.Title);
            _series.RootFolderPath = rootFolder;

            (await Subject.BuildPath(_series, false)).Should().Be(Path.Combine(rootFolder, _series.Title));
        }

        [Test]
        public async Task should_reuse_existing_relative_folder_name()
        {
            var folderName = Path.GetFileName(_series.Path);
            var rootFolder = @"C:\Test\TV2".AsOsAgnostic();

            GivenExistingRootFolder(Path.GetDirectoryName(_series.Path));
            GivenSeriesFolderName(_series.Title);
            _series.RootFolderPath = rootFolder;

            (await Subject.BuildPath(_series, true)).Should().Be(Path.Combine(rootFolder, folderName));
        }

        [Test]
        public async Task should_reuse_existing_relative_folder_structure()
        {
            var existingRootFolder = @"C:\Test\TV".AsOsAgnostic();
            var existingRelativePath = @"S\Series.Title";
            var rootFolder = @"C:\Test\TV2".AsOsAgnostic();

            GivenExistingRootFolder(existingRootFolder);
            GivenSeriesFolderName(_series.Title);
            _series.RootFolderPath = rootFolder;
            _series.Path = Path.Combine(existingRootFolder, existingRelativePath);

            (await Subject.BuildPath(_series, true)).Should().Be(Path.Combine(rootFolder, existingRelativePath));
        }

        [Test]
        public async Task should_use_built_path_for_new_series()
        {
            var rootFolder = @"C:\Test\TV2".AsOsAgnostic();

            GivenSeriesFolderName(_series.Title);
            _series.RootFolderPath = rootFolder;
            _series.Path = null;

            (await Subject.BuildPath(_series, true)).Should().Be(Path.Combine(rootFolder, _series.Title));
        }
    }
}
