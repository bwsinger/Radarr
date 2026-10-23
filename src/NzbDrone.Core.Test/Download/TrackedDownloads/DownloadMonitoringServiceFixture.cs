using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class DownloadMonitoringServiceFixture : CoreTest<DownloadMonitoringService>
    {
        [TestCase(TrackedDownloadState.Failed, true, true, true)]
        [TestCase(TrackedDownloadState.Failed, false, true, false)]
        [TestCase(TrackedDownloadState.Failed, true, false, false)]
        [TestCase(TrackedDownloadState.Ignored, true, true, false)]
        public void should_retry_only_current_observed_custom_failure(TrackedDownloadState state, bool preserveFiles, bool observed, bool expected)
        {
            var definition = new DownloadClientDefinition { Id = 1 };
            var item = new DownloadClientItem { DownloadId = "hash" };
            var tracked = new TrackedDownload { State = state, PreserveFilesOnFailure = preserveFiles, DownloadItem = item };
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition).Returns(definition);
            Mocker.GetMock<IDownloadClient>().Setup(c => c.GetItems())
                .Returns(observed ? new[] { item } : Array.Empty<DownloadClientItem>());
            Mocker.GetMock<IDownloadClientFactory>().Setup(f => f.DownloadHandlingEnabled(true))
                .Returns(new List<IDownloadClient> { Mocker.GetMock<IDownloadClient>().Object });
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.TrackDownload(definition, item)).Returns(tracked);

            Subject.Execute(new RefreshMonitoredDownloadsCommand());
            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<DownloadCanBeRemovedEvent>(v => v.TrackedDownload == tracked)), expected ? Times.Exactly(2) : Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
        }

        [Test]
        public void should_continue_after_cleanup_handler_error()
        {
            var definition = new DownloadClientDefinition { Id = 1 };
            var first = new DownloadClientItem { DownloadId = "first" };
            var second = new DownloadClientItem { DownloadId = "second" };
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition).Returns(definition);
            Mocker.GetMock<IDownloadClient>().Setup(c => c.GetItems()).Returns(new[] { first, second });
            Mocker.GetMock<IDownloadClientFactory>().Setup(f => f.DownloadHandlingEnabled(true))
                .Returns(new List<IDownloadClient> { Mocker.GetMock<IDownloadClient>().Object });
            Mocker.GetMock<ITrackedDownloadService>().Setup(s => s.TrackDownload(definition, It.IsAny<DownloadClientItem>()))
                .Returns<DownloadClientDefinition, DownloadClientItem>((d, i) => new TrackedDownload { DownloadItem = i, State = TrackedDownloadState.Failed, PreserveFilesOnFailure = true });
            Mocker.GetMock<IEventAggregator>()
                .Setup(e => e.PublishEvent(It.Is<DownloadCanBeRemovedEvent>(v => v.TrackedDownload.DownloadItem == first)))
                .Throws(new Exception("client unavailable"));

            Subject.Execute(new RefreshMonitoredDownloadsCommand());

            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.Is<DownloadCanBeRemovedEvent>(v => v.TrackedDownload.DownloadItem == second)), Times.Once());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_hold_a_cold_history_error_until_the_next_verified_poll()
        {
            var definition = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = new DownloadClientItem { DownloadId = "hash", Title = "unparseable", Status = DownloadItemStatus.Completed, DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" } };
            var durable = new DownloadHistory { MovieId = 42, DownloadClientId = 1, EventType = DownloadHistoryEventType.DownloadFailed };
            var failure = new MovieHistory { MovieId = 42, EventType = MovieHistoryEventType.DownloadFailed };
            failure.Data.Add("devRecovery", "minimum-format-score");
            Mocker.GetMock<IDownloadClient>().SetupGet(c => c.Definition).Returns(definition);
            Mocker.GetMock<IDownloadClient>().Setup(c => c.GetItems()).Returns(new[] { item });
            Mocker.GetMock<IDownloadClientFactory>().Setup(f => f.DownloadHandlingEnabled(true))
                .Returns(new List<IDownloadClient> { Mocker.GetMock<IDownloadClient>().Object });
            Mocker.GetMock<IDownloadHistoryService>().SetupSequence(s => s.GetLatestDownloadHistoryItem("hash"))
                .Throws(new Exception("temporary history error")).Returns(durable);
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestEvent("hash")).Returns(durable);
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("hash")).Returns(new List<MovieHistory> { failure });
            var tracker = Mocker.Resolve<TrackedDownloadService>();
            Mocker.SetConstant<ITrackedDownloadService>(tracker);

            Subject.Execute(new RefreshMonitoredDownloadsCommand());
            tracker.Find("hash").Should().BeNull();
            Subject.Execute(new RefreshMonitoredDownloadsCommand());
            tracker.Find("hash").State.Should().Be(TrackedDownloadState.Failed);
            Mocker.GetMock<ICompletedDownloadService>().Verify(s => s.Check(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IFailedDownloadService>().Verify(s => s.Check(It.IsAny<TrackedDownload>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.IsAny<DownloadCanBeRemovedEvent>()), Times.Once());
            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.IsAny<DownloadFailedEvent>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
