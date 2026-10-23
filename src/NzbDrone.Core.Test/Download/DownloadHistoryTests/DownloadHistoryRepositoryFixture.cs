using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.DownloadHistoryTests
{
    [TestFixture]
    public class DownloadHistoryRepositoryFixture : DbTest<DownloadHistoryRepository, DownloadHistory>
    {
        private Movie _movie1;
        private Movie _movie2;

        [SetUp]
        public void Setup()
        {
            _movie1 = Builder<Movie>.CreateNew()
                                     .With(s => s.Id = 7)
                                     .Build();

            _movie2 = Builder<Movie>.CreateNew()
                                     .With(s => s.Id = 8)
                                     .Build();
        }

        [Test]
        public void should_delete_history_items_by_movieId()
        {
            var items = Builder<DownloadHistory>.CreateListOfSize(5)
                .TheFirst(1)
                .With(c => c.Id = 0)
                .With(c => c.MovieId = _movie2.Id)
                .TheRest()
                .With(c => c.Id = 0)
                .With(c => c.MovieId = _movie1.Id)
                .BuildListOfNew();

            Db.InsertMany(items);

            Subject.DeleteByMovieIds(new List<int> { _movie1.Id });

            var removedItems = Subject.All().Where(h => h.MovieId == _movie1.Id);
            var nonRemovedItems = Subject.All().Where(h => h.MovieId == _movie2.Id);

            removedItems.Should().HaveCount(0);
            nonRemovedItems.Should().HaveCount(1);
        }

        [Test]
        public void should_return_newer_grab_before_failure_when_dates_match()
        {
            var date = System.DateTime.UtcNow;
            var failed = new DownloadHistory { DownloadId = "hash", SourceTitle = "torrent", Date = date, EventType = DownloadHistoryEventType.DownloadFailed };
            var regrabbed = new DownloadHistory { DownloadId = "hash", SourceTitle = "torrent", Date = date, EventType = DownloadHistoryEventType.DownloadGrabbed };
            Db.Insert(failed);
            Db.Insert(regrabbed);

            Subject.FindByDownloadId("hash").Select(h => h.EventType)
                .Should().Equal(DownloadHistoryEventType.DownloadGrabbed, DownloadHistoryEventType.DownloadFailed);
        }

        [TestCase(42, 42, 1, true)]
        [TestCase(42, 43, 1, false)]
        [TestCase(0, 0, 1, false)]
        [TestCase(42, 0, 1, false)]
        [TestCase(42, 42, 2, false)]
        public void should_authorize_cleanup_from_serialized_history_until_newer_file_import(int downloadMovieId, int historyMovieId, int historyClientId, bool expected)
        {
            var date = System.DateTime.UtcNow;
            Db.Insert(new DownloadHistory { DownloadId = "hash", MovieId = downloadMovieId, DownloadClientId = historyClientId, SourceTitle = "unparseable", Date = date, EventType = DownloadHistoryEventType.DownloadFailed });
            var failure = new MovieHistory { DownloadId = "hash", MovieId = historyMovieId, SourceTitle = "unparseable", Date = date, EventType = MovieHistoryEventType.DownloadFailed, Quality = new QualityModel(), Languages = new List<Language>() };
            failure.Data.Add("DevRecovery", "minimum-format-score");
            Db.Insert(failure);
            var historyRepository = Mocker.Resolve<HistoryRepository>();
            historyRepository.FindByDownloadId("hash").Single().Data.Should().ContainKey("devRecovery");
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("hash"))
                .Returns(() => historyRepository.FindByDownloadId("hash"));
            Mocker.SetConstant<IDownloadHistoryRepository>(Subject);
            var history = Mocker.Resolve<DownloadHistoryService>();
            Mocker.SetConstant<IDownloadHistoryService>(history);
            var tracker = Mocker.Resolve<TrackedDownloadService>();
            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = new DownloadClientItem { DownloadId = "hash", Title = "unparseable", DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" } };
            tracker.TrackDownload(client, item).PreserveFilesOnFailure.Should().Be(expected);

            // FileImported intentionally does not replace native terminal state, but must stop cleanup.
            Db.Insert(new DownloadHistory { DownloadId = "hash", MovieId = downloadMovieId, DownloadClientId = historyClientId, SourceTitle = "unparseable", Date = date, EventType = DownloadHistoryEventType.FileImported });
            history.GetLatestDownloadHistoryItem("hash").EventType.Should().Be(DownloadHistoryEventType.DownloadFailed);
            history.GetLatestEvent("hash").EventType.Should().Be(DownloadHistoryEventType.FileImported);
            var superseded = tracker.TrackDownload(client, item);
            superseded.State.Should().Be(TrackedDownloadState.Failed);
            superseded.PreserveFilesOnFailure.Should().BeFalse();
        }
    }
}
