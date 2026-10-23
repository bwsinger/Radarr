using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Manual;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    [TestFixture]

    // TODO: Add tests to ensure helpers for augmenters are correctly passed.
    public class ImportDecisionMakerFixture : CoreTest<ImportDecisionMaker>
    {
        private List<string> _videoFiles;
        private LocalMovie _localMovie;
        private Movie _movie;
        private QualityModel _quality;
        private ParsedMovieInfo _fileInfo;

        private Mock<IImportDecisionEngineSpecification> _pass1;
        private Mock<IImportDecisionEngineSpecification> _pass2;
        private Mock<IImportDecisionEngineSpecification> _pass3;

        private Mock<IImportDecisionEngineSpecification> _fail1;
        private Mock<IImportDecisionEngineSpecification> _fail2;
        private Mock<IImportDecisionEngineSpecification> _fail3;

        [SetUp]
        public void Setup()
        {
            _pass1 = new Mock<IImportDecisionEngineSpecification>();
            _pass2 = new Mock<IImportDecisionEngineSpecification>();
            _pass3 = new Mock<IImportDecisionEngineSpecification>();

            _fail1 = new Mock<IImportDecisionEngineSpecification>();
            _fail2 = new Mock<IImportDecisionEngineSpecification>();
            _fail3 = new Mock<IImportDecisionEngineSpecification>();

            _pass1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());
            _pass2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());
            _pass3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Accept());

            _fail1.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail1"));
            _fail2.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail2"));
            _fail3.Setup(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>())).Returns(ImportSpecDecision.Reject(ImportRejectionReason.Unknown, "_fail3"));

            _movie = Builder<Movie>.CreateNew()
                                     .With(e => e.Path = @"C:\Test\Movie".AsOsAgnostic())
                                     .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                     .Build();

            _quality = new QualityModel(Quality.DVD);

            _localMovie = new LocalMovie
            {
                Movie = _movie,
                Quality = _quality,
                Path = @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.avi"
            };

            _fileInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { "The Movie" },
                Year = 2018,
                Quality = _quality
            };

            Mocker.GetMock<IParsingService>()
                .Setup(c => c.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                .Returns(_fileInfo);

            GivenVideoFiles(new List<string> { @"C:\Test\Unsorted\The.Movie.2018.DVDRip.XviD-OSiTV.avi".AsOsAgnostic() });
        }

        private void GivenSpecifications(params Mock<IImportDecisionEngineSpecification>[] mocks)
        {
            Mocker.SetConstant(mocks.Select(c => c.Object));
        }

        private void GivenVideoFiles(IEnumerable<string> videoFiles)
        {
            _videoFiles = videoFiles.ToList();

            Mocker.GetMock<IMediaFileService>()
                  .Setup(c => c.FilterExistingFiles(_videoFiles, It.IsAny<Movie>()))
                  .Returns(_videoFiles);
        }

        private void GivenAugmentationSuccess()
        {
            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Callback<LocalMovie, DownloadClientItem>((localMovie, downloadClientItem) =>
                  {
                      localMovie.Movie = _localMovie.Movie;
                  });
        }

        [Test]
        public void should_call_all_specifications()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            Subject.GetImportDecisions(_videoFiles, _movie, downloadClientItem, null, false, true);

            _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
            _pass3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalMovie>(), downloadClientItem), Times.Once());
        }

        [TestCase(1, false)]
        [TestCase(2, true)]
        public void should_preserve_known_video_file_context_for_runtime_observation(int count, bool multiple)
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1);
            GivenVideoFiles(Enumerable.Range(0, count).Select(i => $"/downloads/Movie.2018.part{i}.mkv"));
            Mocker.GetMock<IDetectSample>().Setup(s => s.IsSample(It.IsAny<MovieMetadata>(), It.IsAny<string>()))
                .Returns(DetectSampleResult.NotSample);

            var decisions = Subject.GetImportDecisions(_videoFiles, _movie);

            decisions.Should().HaveCount(count);
            decisions.Should().OnlyContain(d => d.LocalMovie.OtherVideoFilesKnown && d.LocalMovie.OtherVideoFiles == multiple);
        }

        [Test]
        public void should_keep_partial_file_inventory_unknown_for_specifications()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1);
            var decisions = Subject.GetImportDecisions(_videoFiles, _movie, null, null, true, true, false);
            decisions.Single().LocalMovie.OtherVideoFilesKnown.Should().BeFalse();
            _pass1.Verify(s => s.IsSatisfiedBy(It.Is<LocalMovie>(m => !m.OtherVideoFilesKnown), null), Times.Once());
        }

        [Test]
        public void should_keep_individual_manual_previews_unknown_in_an_unmapped_folder()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1);
            var folder = "/downloads/misc";
            var files = new[] { folder + "/Movie.2018.part1.mkv", folder + "/Movie.2018.part2.mkv" };
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(s => s.FolderExists(folder)).Returns(true);
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(s => s.GetDirectories(folder)).Returns(System.Array.Empty<string>());
            Mocker.GetMock<IDiskScanService>().Setup(s => s.GetVideoFiles(folder, false)).Returns(files);
            Mocker.GetMock<IDiskScanService>().Setup(s => s.FilterPaths(folder, It.IsAny<IEnumerable<string>>(), true))
                .Returns<string, IEnumerable<string>, bool>((basePath, paths, filterExtras) => paths.ToList());
            Mocker.GetMock<IParsingService>().Setup(s => s.GetMovie(It.Is<string>(p => p.EndsWith(".mkv")))).Returns(_movie);
            Mocker.GetMock<IMediaFileService>().Setup(s => s.FilterExistingFiles(It.IsAny<List<string>>(), _movie))
                .Returns<List<string>, Movie>((paths, movie) => paths);
            Mocker.SetConstant<IMakeImportDecision>(Subject);

            Mocker.Resolve<ManualImportService>().GetMediaFiles(folder, null, null, true).Should().HaveCount(2);
            _pass1.Verify(s => s.IsSatisfiedBy(It.Is<LocalMovie>(m => !m.OtherVideoFilesKnown), null), Times.Exactly(2));
        }

        [Test]
        public void should_keep_individual_automatic_file_scans_unknown_for_runtime_observation()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1);
            var file = "/downloads/Movie.2018.part1.mkv";
            GivenVideoFiles(new[] { file });
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(s => s.FolderExists(file)).Returns(false);
            Mocker.GetMock<NzbDrone.Common.Disk.IDiskProvider>().Setup(s => s.FileExists(file)).Returns(true);
            Mocker.GetMock<IImportApprovedMovie>().Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), true, null, ImportMode.Auto))
                .Returns(new List<ImportResult>());
            Mocker.SetConstant<IMakeImportDecision>(Subject);

            Mocker.Resolve<DownloadedMovieImportService>().ProcessPath(file, ImportMode.Auto, _movie);

            _pass1.Verify(s => s.IsSatisfiedBy(It.Is<LocalMovie>(m => m.Path == file && !m.OtherVideoFilesKnown), null), Times.Once());
        }

        [Test]
        public void should_return_rejected_if_single_specs_fail()
        {
            GivenSpecifications(_fail1);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_rejected_if_one_of_specs_fail()
        {
            GivenSpecifications(_pass1, _fail1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeFalse();
        }

        [Test]
        public void should_return_approved_if_all_specs_pass()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);

            result.Single().Approved.Should().BeTrue();
        }

        [Test]
        public void should_have_same_number_of_rejections_as_specs_that_failed()
        {
            GivenAugmentationSuccess();
            GivenSpecifications(_pass1, _pass2, _pass3, _fail1, _fail2, _fail3);

            var result = Subject.GetImportDecisions(_videoFiles, _movie);
            result.Single().Rejections.Should().HaveCount(3);
        }

        [Test]
        public void should_not_blowup_the_process_due_to_failed_parse()
        {
            GivenSpecifications(_pass1);

            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();

            _videoFiles = new List<string>
                {
                    "The.Movie.2021.DVDRip.XviD-OSiTV",
                    "The.Movie.2021.DVDRip.XviD-OSiTV",
                    "The.Movie.2021.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _movie);

            Mocker.GetMock<IAggregationService>()
                  .Verify(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()), Times.Exactly(_videoFiles.Count));

            ExceptionVerification.ExpectedErrors(3);
        }

        [Test]
        public void should_call_parsing_service_with_filename_as_simpletitle()
        {
            GivenSpecifications(_pass1, _pass2, _pass3);

            Mocker.GetMock<IParsingService>()
                .Setup(c => c.ParseMinimalPathMovieInfo(It.IsAny<string>()))
                .Returns<ParsedMovieInfo>(null);

            var folderInfo = new ParsedMovieInfo { SimpleReleaseTitle = "A Movie Folder 2018", Quality = _quality };

            var result = Subject.GetImportDecisions(_videoFiles, _movie, null, folderInfo, true);

            var fileNames = _videoFiles.Select(System.IO.Path.GetFileName);

            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();
        }

        [Test]
        public void should_return_a_decision_when_exception_is_caught()
        {
            Mocker.GetMock<IAggregationService>()
                  .Setup(c => c.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Throws<TestException>();

            _videoFiles = new List<string>
                {
                    "The.Movie.2021.DVDRip.XviD-OSiTV"
                };

            GivenVideoFiles(_videoFiles);

            Subject.GetImportDecisions(_videoFiles, _movie).Should().HaveCount(1);

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
