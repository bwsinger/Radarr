using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class DownloadEventHubFixture : CoreTest<DownloadEventHub>
    {
        [TestCase(true)]
        [TestCase(false)]
        public void should_preserve_payload_for_automatic_below_minimum_failure(bool preserveFiles)
        {
            var item = new DownloadClientItem
            {
                DownloadId = "torrent-hash",
                CanBeRemoved = true,
                DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" }
            };
            var tracked = new TrackedDownload
            {
                DownloadClient = 1,
                DownloadItem = item,
                PreserveFilesOnFailure = preserveFiles
            };
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition)
                .Returns(new DownloadClientDefinition { RemoveFailedDownloads = true });
            Mocker.GetMock<IProvideDownloadClient>().Setup(p => p.Get(1))
                .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Subject.Handle(new DownloadFailedEvent { TrackedDownload = tracked });

            Mocker.GetMock<IDownloadClient>().Verify(c => c.RemoveItem(item, !preserveFiles), Times.Once());
            item.Removed.Should().BeTrue();
        }

        [Test]
        public void should_honor_disabled_failed_download_removal()
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 1,
                DownloadItem = new DownloadClientItem { CanBeRemoved = true },
                PreserveFilesOnFailure = true
            };
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition)
                .Returns(new DownloadClientDefinition { RemoveFailedDownloads = false });
            Mocker.GetMock<IProvideDownloadClient>().Setup(p => p.Get(1))
                .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Subject.Handle(new DownloadFailedEvent { TrackedDownload = tracked });

            Mocker.GetMock<IDownloadClient>().Verify(c => c.RemoveItem(It.IsAny<DownloadClientItem>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
