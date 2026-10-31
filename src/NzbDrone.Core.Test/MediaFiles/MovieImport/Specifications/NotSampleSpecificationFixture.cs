using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    [TestFixture]
    public class NotSampleSpecificationFixture : CoreTest<NotSampleSpecification>
    {
        private Movie _movie;
        private LocalMovie _localEpisode;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                     .Build();

            _localEpisode = new LocalMovie
            {
                Path = @"C:\Test\30 Rock\30.rock.s01e01.avi",
                Movie = _movie,
            };
        }

        [Test]
        public void should_return_true_for_existing_file()
        {
            _localEpisode.ExistingFile = true;
            Subject.IsSatisfiedBy(_localEpisode, null).Accepted.Should().BeTrue();
        }

        [TestCase(true, DownloadItemStatus.Completed, 0L, ImportRejectionReason.ZeroFilledVideo)]
        [TestCase(false, DownloadItemStatus.Completed, 0L, ImportRejectionReason.SampleIndeterminate)]
        [TestCase(true, DownloadItemStatus.Downloading, 0L, ImportRejectionReason.SampleIndeterminate)]
        [TestCase(true, DownloadItemStatus.Completed, 1L, ImportRejectionReason.SampleIndeterminate)]
        public void should_classify_only_verified_completed_zero_filled_video(bool zeroFilled, DownloadItemStatus status, long remaining, ImportRejectionReason reason)
        {
            Mocker.GetMock<IDetectSample>().Setup(d => d.IsSample(_movie.MovieMetadata, _localEpisode.Path)).Returns(DetectSampleResult.Indeterminate);
            Mocker.GetMock<IZeroFilledVideoDetector>().Setup(d => d.IsZeroFilled(_localEpisode.Path, _localEpisode.Size)).Returns(zeroFilled);

            Subject.IsSatisfiedBy(_localEpisode, new DownloadClientItem { Status = status, RemainingSize = remaining })
                .Reason.Should().Be(reason);
        }

        [Test]
        public void should_leave_manual_probe_failure_for_review()
        {
            Mocker.GetMock<IDetectSample>().Setup(d => d.IsSample(_movie.MovieMetadata, _localEpisode.Path)).Returns(DetectSampleResult.Indeterminate);
            Subject.IsSatisfiedBy(_localEpisode, null).Reason.Should().Be(ImportRejectionReason.SampleIndeterminate);
            Mocker.GetMock<IZeroFilledVideoDetector>().Verify(d => d.IsZeroFilled(It.IsAny<string>(), It.IsAny<long>()), Times.Never());
        }
    }
}
