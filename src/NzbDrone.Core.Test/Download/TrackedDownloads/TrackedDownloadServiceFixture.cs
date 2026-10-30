using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.TorrentRss;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Manual;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download.TrackedDownloads
{
    [TestFixture]
    public class TrackedDownloadServiceFixture : CoreTest<TrackedDownloadService>
    {
        [SetUp]
        public void Setup()
        {
        }

        private void GivenDownloadHistory()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns(new List<MovieHistory>()
                {
                    new MovieHistory()
                    {
                        DownloadId = "35238",
                        SourceTitle = "TV Series S01",
                        MovieId = 3,
                    }
                });
        }

        [Test]
        public void should_track_downloads_using_the_source_title_if_it_cannot_be_found_using_the_download_title()
        {
            GivenDownloadHistory();

            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie() { Id = 3 },

                ParsedMovieInfo = new ParsedMovieInfo()
                {
                    MovieTitles = new List<string> { "A Movie" },
                    Year = 1998
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.Is<ParsedMovieInfo>(i => i.PrimaryMovieTitle == "A Movie"), It.IsAny<string>(), It.IsAny<int>(), null))
                  .Returns(remoteMovie);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "A Movie 1998",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteMovie.Should().NotBeNull();
            trackedDownload.RemoteMovie.Movie.Should().NotBeNull();
            trackedDownload.RemoteMovie.Movie.Id.Should().Be(3);
        }

        [Test]
        public void should_set_indexer()
        {
            var episodeHistory = new MovieHistory()
            {
                DownloadId = "35238",
                SourceTitle = "TV Series S01",
                MovieId = 3,
                EventType = MovieHistoryEventType.Grabbed,
            };
            episodeHistory.Data.Add("indexer", "MyIndexer (Prowlarr)");
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns(new List<MovieHistory>()
                {
                    episodeHistory
                });

            var indexerDefinition = new IndexerDefinition
            {
                Id = 1,
                Name = "MyIndexer (Prowlarr)",
                Settings = new TorrentRssIndexerSettings { MultiLanguages = new List<int> { Language.Original.Id, Language.French.Id } }
            };
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.Get(indexerDefinition.Id))
                .Returns(indexerDefinition);
            Mocker.GetMock<IIndexerFactory>()
                .Setup(v => v.All())
                .Returns(new List<IndexerDefinition>() { indexerDefinition });

            var remoteEpisode = new RemoteMovie
            {
                Movie = new Movie() { Id = 3 },
                ParsedMovieInfo = new ParsedMovieInfo()
                {
                    MovieTitles = new List<string> { "A Movie" },
                    Year = 1998
                }
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), null))
                .Returns(remoteEpisode);

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "A Movie 1998",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteMovie.Should().NotBeNull();
            trackedDownload.RemoteMovie.Release.Should().NotBeNull();
            trackedDownload.RemoteMovie.Release.Indexer.Should().Be("MyIndexer (Prowlarr)");
        }

        [Test]
        public void should_unmap_tracked_download_if_movie_deleted()
        {
            GivenDownloadHistory();

            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie() { Id = 3 },

                ParsedMovieInfo = new ParsedMovieInfo()
                {
                    MovieTitles = { "A Movie" },
                    Year = 1998
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), null))
                  .Returns(remoteMovie);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<MovieHistory>());

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "A Movie 1998",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), null))
                  .Returns(default(RemoteMovie));

            Subject.Handle(new MoviesDeletedEvent(new List<Movie> { remoteMovie.Movie }, false, false));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteMovie.Should().BeNull();
        }

        [Test]
        public void should_not_throw_when_processing_deleted_movie()
        {
            GivenDownloadHistory();

            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie() { Id = 3 },

                ParsedMovieInfo = new ParsedMovieInfo()
                {
                    MovieTitles = { "A Movie" },
                    Year = 1998
                }
            };

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), null))
                  .Returns(default(RemoteMovie));

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<MovieHistory>());

            var client = new DownloadClientDefinition()
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem()
            {
                Title = "A Movie 1998",
                DownloadId = "12345",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Id = 1,
                    Type = "Blackhole",
                    Name = "Blackhole Client",
                    Protocol = DownloadProtocol.Torrent
                }
            };

            Subject.TrackDownload(client, item);
            Subject.GetTrackedDownloads().Should().HaveCount(1);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.Map(It.IsAny<ParsedMovieInfo>(), It.IsAny<string>(), It.IsAny<int>(), null))
                  .Returns(default(RemoteMovie));

            Subject.Handle(new MoviesDeletedEvent(new List<Movie> { remoteMovie.Movie }, false, false));

            var trackedDownloads = Subject.GetTrackedDownloads();
            trackedDownloads.Should().HaveCount(1);
            trackedDownloads.First().RemoteMovie.Should().BeNull();
        }

        [Test]
        public void should_retrack_as_downloading_when_grabbed_again_after_being_imported()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([]);

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(It.Is<string>(sr => sr == "35238")))
                .Returns(new DownloadHistory
                {
                    MovieId = 5,
                    EventType = DownloadHistoryEventType.DownloadImported,
                });

            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie { Id = 5 },
                ParsedMovieInfo = new ParsedMovieInfo
                {
                    MovieTitles = { "A Movie" },
                    Year = 1998
                },
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedMovieInfo>(i => i.Year == 1998 && i.MovieTitle == "A Movie"), It.IsAny<int>()))
                .Returns(remoteMovie);

            var client = new DownloadClientDefinition
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem
            {
                Title = "A Movie 1998",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var imported = Subject.TrackDownload(client, item);
            imported.State.Should().Be(TrackedDownloadState.Imported);

            Subject.Handle(new MovieGrabbedEvent(remoteMovie) { DownloadId = "35238" });

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(It.Is<string>(sr => sr == "35238")))
                .Returns(new DownloadHistory
                {
                    MovieId = 5,
                    EventType = DownloadHistoryEventType.DownloadGrabbed,
                });

            var regrabbed = Subject.TrackDownload(client, item);
            regrabbed.State.Should().Be(TrackedDownloadState.Downloading);
        }

        [Test]
        public void should_track_downloads_using_the_movie_id_for_already_imported_downloads()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
                .Returns([]);

            Mocker.GetMock<IDownloadHistoryService>()
                .Setup(s => s.GetLatestDownloadHistoryItem(It.Is<string>(sr => sr == "35238")))
                .Returns(new DownloadHistory
                {
                    MovieId = 5,
                    EventType = DownloadHistoryEventType.DownloadImported,
                });

            var remoteMovie = new RemoteMovie
            {
                Movie = new Movie { Id = 5 },
                ParsedMovieInfo = new ParsedMovieInfo
                {
                    MovieTitles = { "A Movie" },
                    Year = 1998
                },
            };

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.Map(It.Is<ParsedMovieInfo>(i => i.Year == 1998 && i.MovieTitle == "A Movie"), It.IsAny<int>()))
                .Returns(remoteMovie);

            var client = new DownloadClientDefinition
            {
                Id = 1,
                Protocol = DownloadProtocol.Torrent
            };

            var item = new DownloadClientItem
            {
                Title = "A Movie 1998",
                DownloadId = "35238",
                DownloadClientInfo = new DownloadClientItemClientInfo
                {
                    Protocol = client.Protocol,
                    Id = client.Id,
                    Name = client.Name
                }
            };

            var trackedDownload = Subject.TrackDownload(client, item);

            trackedDownload.Should().NotBeNull();
            trackedDownload.RemoteMovie.Should().NotBeNull();
            trackedDownload.RemoteMovie.Movie.Should().NotBeNull();
            trackedDownload.RemoteMovie.Movie.Id.Should().Be(5);
            trackedDownload.RemoteMovie.ParsedMovieInfo.Year.Should().Be(1998);
        }

        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, true, 1, true, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, true, 1, true, true)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, false, 1, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, false, 1, false, true)]
        [TestCase(DownloadHistoryEventType.DownloadGrabbed, MovieHistoryEventType.DownloadFailed, true, 1, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadGrabbed, MovieHistoryEventType.DownloadFailed, true, 1, false, true)]
        [TestCase(DownloadHistoryEventType.DownloadImported, MovieHistoryEventType.DownloadFailed, true, 1, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadImported, MovieHistoryEventType.DownloadFailed, true, 1, false, true)]
        [TestCase(DownloadHistoryEventType.DownloadIgnored, MovieHistoryEventType.DownloadFailed, true, 1, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadIgnored, MovieHistoryEventType.DownloadFailed, true, 1, false, true)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.Grabbed, true, 1, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.Grabbed, true, 1, false, true)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFolderImported, true, 1, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFolderImported, true, 1, false, true)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, true, 2, false, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, true, 2, false, true)]
        public void should_restore_cleanup_only_for_latest_custom_failure(DownloadHistoryEventType downloadEvent, MovieHistoryEventType historyEvent, bool tagged, int historyClient, bool expected, bool serialized, string recovery = "minimum-format-score")
        {
            var olderFailure = new MovieHistory
            {
                Id = 1,
                MovieId = 42,
                EventType = MovieHistoryEventType.DownloadFailed,
                SourceTitle = "unparseable",
                Date = System.DateTime.UtcNow
            };
            olderFailure.Data.Add("DevRecovery", "minimum-format-score");
            var latest = new MovieHistory
            {
                Id = 2,
                MovieId = 42,
                EventType = historyEvent,
                SourceTitle = "unparseable",
                Date = olderFailure.Date
            };
            if (tagged)
            {
                latest.Data.Add("DevRecovery", recovery);
            }

            if (serialized)
            {
                var converter = new EmbeddedDocumentConverter<Dictionary<string, string>>();
                var parameter = new SQLiteParameter();
                converter.SetValue(parameter, olderFailure.Data);
                olderFailure.Data = converter.Parse(parameter.Value);
                converter.SetValue(parameter, latest.Data);
                latest.Data = converter.Parse(parameter.Value);
                olderFailure.Data.Should().ContainKey("devRecovery");
            }

            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("hash"))
                .Returns(new List<MovieHistory> { olderFailure, latest });
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestDownloadHistoryItem("hash"))
                .Returns(new DownloadHistory { EventType = downloadEvent, DownloadClientId = historyClient, MovieId = 42 });
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestEvent("hash"))
                .Returns(new DownloadHistory { EventType = downloadEvent, DownloadClientId = historyClient, MovieId = 42 });
            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = new DownloadClientItem
            {
                DownloadId = "hash",
                Title = "unparseable",
                CanBeRemoved = false,
                DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" }
            };

            var tracked = Subject.TrackDownload(client, item);
            tracked.PreserveFilesOnFailure.Should().Be(expected);
            tracked.HasZeroFilledVideo.Should().Be(expected && recovery == "zero-filled-video");
            tracked.DownloadItem.CanBeRemoved.Should().BeFalse();

            // A new durable grab also supersedes a cached failure, without publishing another failure.
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestDownloadHistoryItem("hash"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, DownloadClientId = 1 });
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestEvent("hash"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadGrabbed, DownloadClientId = 1, MovieId = 42 });
            if (expected)
            {
                Subject.TrackDownload(client, item).PreserveFilesOnFailure.Should().BeFalse();
            }
        }

        [TestCase("terminal", false)]
        [TestCase("terminal", true)]
        [TestCase("latest", false)]
        [TestCase("latest", true)]
        [TestCase("movie", false)]
        [TestCase("movie", true)]
        public void should_retry_durable_history_errors_without_caching_guessed_state(string failureSource, bool cachedFailure)
        {
            var failure = new MovieHistory { MovieId = 42, EventType = MovieHistoryEventType.DownloadFailed, SourceTitle = "unparseable" };
            failure.Data.Add("DevRecovery", "minimum-format-score");
            var durable = new DownloadHistory { MovieId = 42, EventType = DownloadHistoryEventType.DownloadFailed, DownloadClientId = 1 };
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestDownloadHistoryItem("hash")).Returns(durable);
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestEvent("hash")).Returns(durable);
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("hash")).Returns(new List<MovieHistory> { failure });
            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = new DownloadClientItem { DownloadId = "hash", Title = "unparseable", DownloadClientInfo = new DownloadClientItemClientInfo { Name = "qBittorrent" } };
            var previous = cachedFailure ? Subject.TrackDownload(client, item) : null;

            if (failureSource == "terminal")
            {
                Mocker.GetMock<IDownloadHistoryService>().SetupSequence(s => s.GetLatestDownloadHistoryItem("hash"))
                    .Throws(new System.Exception("temporary download history error")).Returns(durable);
            }
            else if (failureSource == "latest")
            {
                Mocker.GetMock<IDownloadHistoryService>().SetupSequence(s => s.GetLatestEvent("hash"))
                    .Throws(new System.Exception("temporary download history error")).Returns(durable);
            }
            else
            {
                Mocker.GetMock<IHistoryService>().SetupSequence(s => s.FindByDownloadId("hash"))
                    .Throws(new System.Exception("temporary history error")).Returns(new List<MovieHistory> { failure });
            }

            Subject.Invoking(s => s.TrackDownload(client, item)).Should().Throw<System.Exception>();
            Subject.Find("hash").Should().BeSameAs(previous);
            var restored = Subject.TrackDownload(client, item);
            restored.State.Should().Be(TrackedDownloadState.Failed);
            restored.PreserveFilesOnFailure.Should().BeTrue();
        }

        [TestCase(DownloadHistoryEventType.FileImported, 42, 42, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, 0, 0, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, 42, 0, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, 42, 43, false)]
        [TestCase(DownloadHistoryEventType.DownloadFailed, 42, 42, true)]
        public void should_require_latest_raw_failure_for_the_same_positive_movie(DownloadHistoryEventType latestEvent, int downloadMovieId, int movieHistoryId, bool expected)
        {
            var failure = new MovieHistory { MovieId = movieHistoryId, EventType = MovieHistoryEventType.DownloadFailed };
            failure.Data.Add("devRecovery", "minimum-format-score");
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("hash")).Returns(new List<MovieHistory> { failure });
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestDownloadHistoryItem("hash"))
                .Returns(new DownloadHistory { EventType = DownloadHistoryEventType.DownloadFailed, MovieId = downloadMovieId, DownloadClientId = 1 });
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestEvent("hash"))
                .Returns(new DownloadHistory { EventType = latestEvent, MovieId = downloadMovieId, DownloadClientId = 1 });

            var tracked = Subject.TrackDownload(new DownloadClientDefinition { Id = 1 }, new DownloadClientItem
            {
                DownloadId = "hash", Title = "unparseable", DownloadClientInfo = new DownloadClientItemClientInfo { Name = "client" }
            });
            tracked.State.Should().Be(TrackedDownloadState.Failed);
            tracked.PreserveFilesOnFailure.Should().Be(expected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void should_allow_manual_override_after_failed_refresh(bool restoredAfterRestart)
        {
            var movie = new Movie { Id = 42, Path = "/library/Movie", MovieMetadata = new MovieMetadata { Title = "Movie", Year = 2020 } };
            var failure = new MovieHistory { MovieId = 42, EventType = MovieHistoryEventType.DownloadFailed };
            failure.Data.Add("devRecovery", "minimum-format-score");
            var durable = new DownloadHistory { MovieId = 42, EventType = DownloadHistoryEventType.DownloadFailed, DownloadClientId = 1 };
            Mocker.GetMock<IHistoryService>().Setup(s => s.FindByDownloadId("hash")).Returns(new List<MovieHistory> { failure });
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestDownloadHistoryItem("hash")).Returns(durable);
            Mocker.GetMock<IDownloadHistoryService>().Setup(s => s.GetLatestEvent("hash")).Returns(durable);
            var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
            var item = new DownloadClientItem { DownloadId = "hash", Title = "Movie.2020", OutputPath = new OsPath("/downloads/Movie.2020"), DownloadClientInfo = new DownloadClientItemClientInfo { Id = 1, Name = "qBittorrent" } };
            var importItem = item.Clone();
            if (!restoredAfterRestart)
            {
                var failed = Subject.TrackDownload(client, item);
                failed.ImportItem = importItem;
            }

            var refreshed = Subject.TrackDownload(client, item);
            refreshed.PreserveFilesOnFailure.Should().BeTrue();
            Mocker.SetConstant<ITrackedDownloadService>(Subject);
            Mocker.GetMock<IProvideImportItemService>().Setup(s => s.ProvideImportItem(item, null)).Returns(importItem);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(42)).Returns(movie);
            Mocker.GetMock<IAggregationService>().Setup(s => s.Augment(It.IsAny<LocalMovie>(), item)).Returns<LocalMovie, DownloadClientItem>((local, download) => local);
            Mocker.GetMock<IImportApprovedMovie>().Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), true, item, It.IsAny<ImportMode>()))
                .Returns<List<ImportDecision>, bool, DownloadClientItem, ImportMode>((decisions, newDownload, download, mode) => decisions.Select(d => new ImportResult(d)).ToList());
            var manual = Mocker.Resolve<ManualImportService>();
            manual.GetMediaFiles("", "hash", 42, true).Should().BeEmpty();
            manual.Execute(new ManualImportCommand { Files = new List<ManualImportFile> { new ManualImportFile { Path = "/downloads/Movie.2020/Movie.2020.mkv", MovieId = 42, DownloadId = "hash" } } });

            refreshed.ImportItem.Should().BeSameAs(importItem);
            refreshed.State.Should().Be(TrackedDownloadState.Imported);
            Mocker.GetMock<IProvideImportItemService>().Verify(s => s.ProvideImportItem(item, null), restoredAfterRestart ? Times.Once() : Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(s => s.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());
            Mocker.GetMock<IImportApprovedMovie>().Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d.All(x => x.Approved)), true, item, It.IsAny<ImportMode>()), Times.Once());
        }

        [TestCase("zero-filled-video", DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, 1, true)]
        [TestCase("zero-filled-video", DownloadHistoryEventType.DownloadGrabbed, MovieHistoryEventType.DownloadFailed, 1, false)]
        [TestCase("zero-filled-video", DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.Grabbed, 1, false)]
        [TestCase("zero-filled-video", DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, 2, false)]
        [TestCase("unknown-recovery", DownloadHistoryEventType.DownloadFailed, MovieHistoryEventType.DownloadFailed, 1, false)]
        public void should_restore_only_current_known_zero_filled_recovery(string recovery, DownloadHistoryEventType downloadEvent, MovieHistoryEventType historyEvent, int historyClient, bool expected)
        {
            should_restore_cleanup_only_for_latest_custom_failure(downloadEvent, historyEvent, true, historyClient, expected, true, recovery);
        }
    }
}
