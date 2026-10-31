using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    [TestFixture]
    public class ZeroFilledVideoDetectorFixture : CoreTest<ZeroFilledVideoDetector>
    {
        private const int FileSize = (2 * 1024 * 1024) + 17;
        private string _path;

        [SetUp]
        public void Setup()
        {
            _path = Path.Combine(TempFolder, "video.mkv");
            File.WriteAllBytes(_path, new byte[FileSize]);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.OpenReadStream(It.IsAny<string>()))
                .Returns<string>(File.OpenRead);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileGetLastWrite(It.IsAny<string>()))
                .Returns<string>(File.GetLastWriteTimeUtc);
        }

        [TestCase(".mkv")]
        [TestCase(".MKV")]
        [TestCase(".mp4")]
        [TestCase(".m4v")]
        public void should_identify_zero_filled_supported_containers(string extension)
        {
            var path = Path.ChangeExtension(_path, extension);
            if (path != _path)
            {
                File.Move(_path, path);
            }

            Subject.IsZeroFilled(path, FileSize).Should().BeTrue();
        }

        [TestCase(0)]
        [TestCase(FileSize / 2)]
        [TestCase(FileSize - 1)]
        public void should_not_classify_nonzero_sampled_data(int offset)
        {
            using (var stream = File.OpenWrite(_path))
            {
                stream.Position = offset;
                stream.WriteByte(1);
            }

            Subject.IsZeroFilled(_path, FileSize).Should().BeFalse();
        }

        [TestCase(0)]
        [TestCase(1024)]
        [TestCase(FileSize - 1)]
        [TestCase(FileSize + 1)]
        public void should_not_classify_small_or_mismatched_files(long expectedSize)
        {
            Subject.IsZeroFilled(_path, expectedSize).Should().BeFalse();
        }

        [TestCase(".avi")]
        [TestCase(".ifo")]
        [TestCase(".ts")]
        public void should_leave_other_formats_for_review(string extension)
        {
            Subject.IsZeroFilled(Path.ChangeExtension(_path, extension), FileSize).Should().BeFalse();
            Mocker.GetMock<IDiskProvider>().Verify(d => d.OpenReadStream(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_leave_changing_file_for_review()
        {
            Mocker.GetMock<IDiskProvider>().SetupSequence(d => d.FileGetLastWrite(_path))
                .Returns(DateTime.UtcNow.AddMinutes(-1)).Returns(DateTime.UtcNow);

            Subject.IsZeroFilled(_path, FileSize).Should().BeFalse();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_leave_unreadable_file_for_review(bool denied)
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.OpenReadStream(_path))
                .Throws(denied ? new UnauthorizedAccessException() : new IOException());

            Subject.IsZeroFilled(_path, FileSize).Should().BeFalse();
        }
    }
}
