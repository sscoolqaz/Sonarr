using System.Collections.Generic;
using System.Threading.Tasks;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Organizer
{
    public interface IFilenameSampleService
    {
        Task<SampleResult> GetStandardSample(NamingConfig nameSpec);
        Task<SampleResult> GetMultiEpisodeSample(NamingConfig nameSpec);
        Task<SampleResult> GetDailySample(NamingConfig nameSpec);
        Task<SampleResult> GetAnimeSample(NamingConfig nameSpec);
        Task<SampleResult> GetAnimeMultiEpisodeSample(NamingConfig nameSpec);
        Task<string> GetSeriesFolderSample(NamingConfig nameSpec);
        Task<string> GetSeasonFolderSample(NamingConfig nameSpec);
        Task<string> GetSpecialsFolderSample(NamingConfig nameSpec);
    }

    public class FileNameSampleService : IFilenameSampleService
    {
        private readonly IBuildFileNames _buildFileNames;
        private static Series _standardSeries;
        private static Series _dailySeries;
        private static Series _animeSeries;
        private static Episode _episode1;
        private static Episode _episode2;
        private static Episode _episode3;
        private static List<Episode> _singleEpisode;
        private static List<Episode> _multiEpisodes;
        private static EpisodeFile _singleEpisodeFile;
        private static EpisodeFile _multiEpisodeFile;
        private static EpisodeFile _dailyEpisodeFile;
        private static EpisodeFile _animeEpisodeFile;
        private static EpisodeFile _animeMultiEpisodeFile;
        private static List<CustomFormat> _customFormats;

        public FileNameSampleService(IBuildFileNames buildFileNames)
        {
            _buildFileNames = buildFileNames;

            _standardSeries = new Series
            {
                SeriesType = SeriesTypes.Standard,
                Title = "The Series Title's!",
                Year = 2010,
                ImdbId = "tt12345",
                TvdbId = 12345,
                TvMazeId = 54321,
                TmdbId = 11223
            };

            _dailySeries = new Series
            {
                SeriesType = SeriesTypes.Daily,
                Title = "The Series Title's!",
                Year = 2010,
                ImdbId = "tt12345",
                TvdbId = 12345,
                TvMazeId = 54321,
                TmdbId = 11223
            };

            _animeSeries = new Series
            {
                SeriesType = SeriesTypes.Anime,
                Title = "The Series Title's!",
                Year = 2010,
                ImdbId = "tt12345",
                TvdbId = 12345,
                TvMazeId = 54321,
                TmdbId = 11223
            };

            _episode1 = new Episode
            {
                SeasonNumber = 1,
                EpisodeNumber = 1,
                Title = "Episode Title (1)",
                AirDate = "2013-10-30",
                AbsoluteEpisodeNumber = 1,
            };

            _episode2 = new Episode
            {
                SeasonNumber = 1,
                EpisodeNumber = 2,
                Title = "Episode Title (2)",
                AbsoluteEpisodeNumber = 2
            };

            _episode3 = new Episode
            {
                SeasonNumber = 1,
                EpisodeNumber = 3,
                Title = "Episode Title (3)",
                AbsoluteEpisodeNumber = 3
            };

            _singleEpisode = new List<Episode> { _episode1 };
            _multiEpisodes = new List<Episode> { _episode1, _episode2, _episode3 };

            var mediaInfo = new MediaInfoModel
            {
                VideoFormat = "AVC",
                VideoBitDepth = 10,
                VideoColourPrimaries = "bt2020",
                VideoTransferCharacteristics = "HLG",
                AudioStreams =
                [
                    new MediaInfoAudioStreamModel
                    {
                        Language = "ger",
                        Format = "dts",
                        Channels = 6,
                        ChannelPositions = "5.1",
                    }
                ],
                SubtitleStreams =
                [
                    new MediaInfoSubtitleStreamModel { Language = "eng" },
                    new MediaInfoSubtitleStreamModel { Language = "ger" }
                ],
            };

            var mediaInfoAnime = new MediaInfoModel
            {
                VideoFormat = "AVC",
                VideoBitDepth = 10,
                VideoColourPrimaries = "BT.2020",
                VideoTransferCharacteristics = "HLG",
                AudioStreams =
                [
                    new MediaInfoAudioStreamModel
                    {
                        Language = "jpn",
                        Format = "dts",
                        Channels = 6,
                        ChannelPositions = "5.1",
                    }
                ],
                SubtitleStreams =
                [
                    new MediaInfoSubtitleStreamModel { Language = "jpn" },
                    new MediaInfoSubtitleStreamModel { Language = "eng" }
                ],
            };

            _customFormats = new List<CustomFormat>
            {
                new CustomFormat
                {
                    Name = "Surround Sound",
                    IncludeCustomFormatWhenRenaming = true
                },
                new CustomFormat
                {
                    Name = "x264",
                    IncludeCustomFormatWhenRenaming = true
                }
            };

            _singleEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p, new Revision(2)),
                RelativePath = "The.Series.Title's!.S01E01.1080p.WEBDL.x264-EVOLVE.mkv",
                SceneName = "The.Series.Title's!.S01E01.1080p.WEBDL.x264-EVOLVE",
                ReleaseGroup = "RlsGrp",
                MediaInfo = mediaInfo
            };

            _multiEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p, new Revision(2)),
                RelativePath = "The.Series.Title's!.S01E01-E03.1080p.WEBDL.x264-EVOLVE.mkv",
                SceneName = "The.Series.Title's!.S01E01-E03.1080p.WEBDL.x264-EVOLVE",
                ReleaseGroup = "RlsGrp",
                MediaInfo = mediaInfo,
            };

            _dailyEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p, new Revision(2)),
                RelativePath = "The.Series.Title's!.2013.10.30.1080p.WEBDL.x264-EVOLVE.mkv",
                SceneName = "The.Series.Title's!.2013.10.30.1080p.WEBDL.x264-EVOLVE",
                ReleaseGroup = "RlsGrp",
                MediaInfo = mediaInfo
            };

            _animeEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p, new Revision(2)),
                RelativePath = "[RlsGroup] The Series Title's! - 001 [1080P].mkv",
                SceneName = "[RlsGroup] The Series Title's! - 001 [1080P]",
                ReleaseGroup = "RlsGrp",
                MediaInfo = mediaInfoAnime
            };

            _animeMultiEpisodeFile = new EpisodeFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p, new Revision(2)),
                RelativePath = "[RlsGroup] The Series Title's! - 001 - 103 [1080p].mkv",
                SceneName = "[RlsGroup] The Series Title's! - 001 - 103 [1080p]",
                ReleaseGroup = "RlsGrp",
                MediaInfo = mediaInfoAnime
            };
        }

        public async Task<SampleResult> GetStandardSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = await BuildSample(_singleEpisode, _standardSeries, _singleEpisodeFile, nameSpec, _customFormats),
                Series = _standardSeries,
                Episodes = _singleEpisode,
                EpisodeFile = _singleEpisodeFile
            };

            return result;
        }

        public async Task<SampleResult> GetMultiEpisodeSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = await BuildSample(_multiEpisodes, _standardSeries, _multiEpisodeFile, nameSpec, _customFormats),
                Series = _standardSeries,
                Episodes = _multiEpisodes,
                EpisodeFile = _multiEpisodeFile
            };

            return result;
        }

        public async Task<SampleResult> GetDailySample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = await BuildSample(_singleEpisode, _dailySeries, _dailyEpisodeFile, nameSpec, _customFormats),
                Series = _dailySeries,
                Episodes = _singleEpisode,
                EpisodeFile = _dailyEpisodeFile
            };

            return result;
        }

        public async Task<SampleResult> GetAnimeSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = await BuildSample(_singleEpisode, _animeSeries, _animeEpisodeFile, nameSpec, _customFormats),
                Series = _animeSeries,
                Episodes = _singleEpisode,
                EpisodeFile = _animeEpisodeFile
            };

            return result;
        }

        public async Task<SampleResult> GetAnimeMultiEpisodeSample(NamingConfig nameSpec)
        {
            var result = new SampleResult
            {
                FileName = await BuildSample(_multiEpisodes, _animeSeries, _animeMultiEpisodeFile, nameSpec, _customFormats),
                Series = _animeSeries,
                Episodes = _multiEpisodes,
                EpisodeFile = _animeMultiEpisodeFile
            };

            return result;
        }

        public async Task<string> GetSeriesFolderSample(NamingConfig nameSpec)
        {
            return await _buildFileNames.GetSeriesFolder(_standardSeries, nameSpec);
        }

        public async Task<string> GetSeasonFolderSample(NamingConfig nameSpec)
        {
            return await _buildFileNames.GetSeasonFolder(_standardSeries, _episode1.SeasonNumber, nameSpec);
        }

        public async Task<string> GetSpecialsFolderSample(NamingConfig nameSpec)
        {
            return await _buildFileNames.GetSeasonFolder(_standardSeries, 0, nameSpec);
        }

        private async Task<string> BuildSample(List<Episode> episodes, Series series, EpisodeFile episodeFile, NamingConfig nameSpec, List<CustomFormat> customFormats)
        {
            try
            {
                return await _buildFileNames.BuildFileName(episodes, series, episodeFile, "", nameSpec, customFormats);
            }
            catch (NamingFormatException)
            {
                return string.Empty;
            }
        }
    }
}
