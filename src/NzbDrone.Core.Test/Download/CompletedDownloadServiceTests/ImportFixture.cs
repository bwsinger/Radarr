using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ImportFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.Title = "Drone.1998")
                                                    .Build();

            var remoteMovie = BuildRemoteMovie();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadState.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteMovie = remoteMovie)
                    .Build();

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.MostRecentForDownloadId(_trackedDownload.DownloadItem.DownloadId))
                  .Returns(new MovieHistory());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Drone.1998"))
                  .Returns(remoteMovie.Movie);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<MovieHistory>());

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);
        }

        private RemoteMovie BuildRemoteMovie()
        {
            return new RemoteMovie
            {
                Movie = new Movie()
            };
        }

        private void GivenABadlyNamedDownload()
        {
            _trackedDownload.DownloadItem.DownloadId = "1234";
            _trackedDownload.DownloadItem.Title = "Droned Pilot"; // Set a badly named download
            Mocker.GetMock<IHistoryService>()
               .Setup(s => s.MostRecentForDownloadId(It.Is<string>(i => i == "1234")))
               .Returns(new MovieHistory() { SourceTitle = "Droned 1998" });

            Mocker.GetMock<IParsingService>()
               .Setup(s => s.GetMovie(It.IsAny<string>()))
               .Returns((Movie)null);

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.GetMovie("Droned 1998"))
                .Returns(BuildRemoteMovie().Movie);
        }

        private void GivenSeriesMatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie(It.IsAny<string>()))
                  .Returns(_trackedDownload.RemoteMovie.Movie);
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_rejected()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure"),

                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1999.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure")
                });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent<DownloadCompletedEvent>(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_no_movies_were_parsed()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure"),

                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure")
                });

            _trackedDownload.RemoteMovie.Movie = new Movie();

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_skipped()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" }), "Test Failure"),
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" }), "Test Failure")
                           });

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_mark_as_imported_if_all_movies_were_imported_but_extra_files_were_not()
        {
            GivenSeriesMatch();

            _trackedDownload.RemoteMovie.Movie = new Movie();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
               {
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv", Movie = _trackedDownload.RemoteMovie.Movie })),
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv" }), "Test Failure")
               });

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_mark_as_imported_if_the_download_can_be_tracked_using_the_source_movieid()
        {
            GivenABadlyNamedDownload();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
               {
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv", Movie = _trackedDownload.RemoteMovie.Movie }))
               });

            Mocker.GetMock<IMovieService>()
                  .Setup(v => v.GetMovie(It.IsAny<int>()))
                  .Returns(BuildRemoteMovie().Movie);

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_not_automatically_fail_usenet_download()
        {
            GivenBelowMinimumDownload();
            _trackedDownload.Protocol = DownloadProtocol.Usenet;

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().NotBe(TrackedDownloadState.FailedPending);
            _trackedDownload.PreserveFilesOnFailure.Should().BeFalse();
        }

        private List<ImportResult> GivenBelowMinimumDownload(bool grabbed = true)
        {
            _trackedDownload.PreserveFilesOnFailure = false;
            _trackedDownload.Protocol = DownloadProtocol.Torrent;
            _trackedDownload.RemoteMovie.Movie.Id = 42;
            var results = new List<ImportResult>
            {
                new ImportResult(new ImportDecision(new LocalMovie { Path = "/downloads/movie.mkv", Movie = _trackedDownload.RemoteMovie.Movie }, new ImportRejection(ImportRejectionReason.BelowMinimumCustomFormatScore, "Below minimum")), "Below minimum")
            };

            Mocker.GetMock<IDownloadedMovieImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                .Returns(results);
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(grabbed ? new List<MovieHistory> { new MovieHistory { EventType = MovieHistoryEventType.Grabbed, MovieId = _trackedDownload.RemoteMovie.Movie.Id } } : new List<MovieHistory>());
            return results;
        }

        [Test]
        public void should_fail_completed_below_minimum_download_and_retain_payload()
        {
            GivenBelowMinimumDownload();
            _trackedDownload.DownloadItem.CanBeRemoved = false;

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.FailedPending);
            _trackedDownload.PreserveFilesOnFailure.Should().BeTrue();
            _trackedDownload.DownloadItem.CanBeRemoved.Should().BeTrue();
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());
        }

        [Test]
        public void should_not_fail_download_without_grab_history()
        {
            GivenBelowMinimumDownload(false);

            Subject.Import(_trackedDownload);

            AssertNotImported();
            _trackedDownload.PreserveFilesOnFailure.Should().BeFalse();
        }

        [TestCase(DownloadItemStatus.Downloading)]
        [TestCase(DownloadItemStatus.Paused)]
        public void should_not_fail_unfinished_download(DownloadItemStatus status)
        {
            GivenBelowMinimumDownload();
            _trackedDownload.DownloadItem.Status = status;

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [TestCase(ImportRejectionReason.UnableToParse)]
        [TestCase(ImportRejectionReason.FileLocked)]
        [TestCase(ImportRejectionReason.NotCustomFormatUpgrade)]
        public void should_not_fail_mixed_or_uncertain_results(ImportRejectionReason reason)
        {
            GivenBelowMinimumDownload().Add(new ImportResult(new ImportDecision(new LocalMovie { Path = "/downloads/other.mkv" }, new ImportRejection(reason, "Other rejection")), "Other rejection"));

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_fail_file_with_additional_rejection()
        {
            var results = GivenBelowMinimumDownload();
            results[0] = new ImportResult(new ImportDecision(new LocalMovie { Path = "/downloads/movie.mkv", Movie = _trackedDownload.RemoteMovie.Movie }, new ImportRejection(ImportRejectionReason.BelowMinimumCustomFormatScore, "Below minimum"), new ImportRejection(ImportRejectionReason.DecisionError, "Decision error")), "Rejected");

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_fail_if_another_file_was_imported()
        {
            GivenBelowMinimumDownload().Add(new ImportResult(new ImportDecision(new LocalMovie
            {
                Path = "/downloads/accepted.mkv",
                Movie = _trackedDownload.RemoteMovie.Movie
            })));

            Subject.Import(_trackedDownload);

            AssertImported();
            _trackedDownload.PreserveFilesOnFailure.Should().BeFalse();
        }

        [Test]
        public void should_not_fail_existing_library_file()
        {
            GivenBelowMinimumDownload()[0].ImportDecision.LocalMovie.ExistingFile = true;

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_fail_download_grabbed_for_another_movie()
        {
            GivenBelowMinimumDownload();
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                .Returns(new List<MovieHistory> { new MovieHistory { EventType = MovieHistoryEventType.Grabbed, MovieId = _trackedDownload.RemoteMovie.Movie.Id + 1 } });

            Subject.Import(_trackedDownload);

            AssertNotImported();
            _trackedDownload.PreserveFilesOnFailure.Should().BeFalse();
        }

        [Test]
        public void should_not_fail_import_mapped_to_another_movie()
        {
            GivenBelowMinimumDownload()[0].ImportDecision.LocalMovie.Movie = new Movie { Id = _trackedDownload.RemoteMovie.Movie.Id + 1 };

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        private void AssertNotImported()
        {
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        private void AssertImported()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                .Verify(v => v.ProcessPath(_trackedDownload.DownloadItem.OutputPath.FullPath, ImportMode.Auto, _trackedDownload.RemoteMovie.Movie, _trackedDownload.DownloadItem), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }
    }
}
