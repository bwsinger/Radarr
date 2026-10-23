using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

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

        [TestCase(true, true, false, true, true)]
        [TestCase(false, true, false, true, false)]
        [TestCase(true, false, false, true, false)]
        [TestCase(true, true, true, true, false)]
        [TestCase(true, true, false, false, false)]
        public void should_retry_only_safe_custom_failed_cleanup(bool enabled, bool canRemove, bool removed, bool preserveFiles, bool shouldRemove)
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 1,
                State = TrackedDownloadState.Failed,
                PreserveFilesOnFailure = preserveFiles,
                DownloadItem = new DownloadClientItem
                {
                    CanBeRemoved = canRemove,
                    Removed = removed,
                    DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" }
                }
            };
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition)
                .Returns(new DownloadClientDefinition { RemoveFailedDownloads = enabled, RemoveCompletedDownloads = false });
            Mocker.GetMock<IProvideDownloadClient>().Setup(p => p.Get(1))
                .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Subject.Handle(new DownloadCanBeRemovedEvent(tracked));
            Subject.Handle(new DownloadCanBeRemovedEvent(tracked));

            Mocker.GetMock<IDownloadClient>().Verify(c => c.RemoveItem(tracked.DownloadItem, false), shouldRemove ? Times.Once() : Times.Never());
            Mocker.GetMock<IDownloadClient>().Verify(c => c.RemoveItem(It.IsAny<DownloadClientItem>(), true), Times.Never());
        }

        [Test]
        public void should_leave_failed_cleanup_retryable_after_client_error()
        {
            var tracked = new TrackedDownload
            {
                DownloadClient = 1,
                State = TrackedDownloadState.Failed,
                PreserveFilesOnFailure = true,
                DownloadItem = new DownloadClientItem
                {
                    CanBeRemoved = true,
                    DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" }
                }
            };
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition)
                .Returns(new DownloadClientDefinition { RemoveFailedDownloads = true });
            Mocker.GetMock<IProvideDownloadClient>().Setup(p => p.Get(1))
                .Returns(Mocker.GetMock<IDownloadClient>().Object);
            Mocker.GetMock<IDownloadClient>().SetupSequence(c => c.RemoveItem(tracked.DownloadItem, false))
                .Throws(new System.Exception("transient error"))
                .Throws(new System.Exception("still unavailable"))
                .Pass();

            Subject.Handle(new DownloadCanBeRemovedEvent(tracked));
            tracked.DownloadItem.Removed.Should().BeFalse();
            Subject.Handle(new DownloadCanBeRemovedEvent(tracked));
            tracked.DownloadItem.Removed.Should().BeFalse();
            Subject.Handle(new DownloadCanBeRemovedEvent(tracked));
            tracked.DownloadItem.Removed.Should().BeTrue();
            Mocker.GetMock<IDownloadClient>().Verify(c => c.RemoveItem(tracked.DownloadItem, false), Times.Exactly(3));
            ExceptionVerification.ExpectedErrors(2);
        }
    }
}
