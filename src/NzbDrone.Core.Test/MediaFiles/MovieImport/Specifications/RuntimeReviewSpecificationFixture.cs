using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Manual;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    [TestFixture]
    public class RuntimeReviewSpecificationFixture : CoreTest<RuntimeReviewSpecification>
    {
        private MemoryTarget _target;
        private LoggingRule _rule;
        private LocalMovie _movie;

        [SetUp]
        public void Setup()
        {
            _target = new MemoryTarget { Layout = "${message}" };
            _rule = new LoggingRule("*", LogLevel.Info, _target);
            LogManager.Configuration.LoggingRules.Add(_rule);
            LogManager.ReconfigExistingLoggers();
            _movie = new LocalMovie
            {
                Movie = new Movie { Id = 42, MovieMetadata = new MovieMetadata { Runtime = 100, Title = "Test \"movie\"\nname", TmdbId = 99 } },
                MediaInfo = new MediaInfoModel(),
                OtherVideoFilesKnown = true,
                Path = "/downloads/test.mkv"
            };
        }

        [TearDown]
        public void Cleanup()
        {
            LogManager.Configuration.LoggingRules.Remove(_rule);
            LogManager.ReconfigExistingLoggers();
            _target.Dispose();
        }

        [TestCase(84.999, "outside", true)]
        [TestCase(85, "within", false)]
        [TestCase(100, "within", false)]
        [TestCase(115, "within", false)]
        [TestCase(115.001, "outside", true)]
        [TestCase(0, "unavailable", false)]
        [TestCase(-1, "unavailable", false)]
        public void should_log_symmetric_comparison_without_rejecting(double actual, string comparison, bool wouldHold)
        {
            _movie.MediaInfo.RunTime = TimeSpan.FromMinutes(actual);
            Subject.IsSatisfiedBy(_movie, new DownloadClientItem { DownloadId = "hash", Title = "release" }).Accepted.Should().BeTrue();
            using var log = Observation();
            log.RootElement.GetProperty("comparison").GetString().Should().Be(comparison);
            log.RootElement.GetProperty("wouldHold").GetBoolean().Should().Be(wouldHold);
            log.RootElement.GetProperty("tolerancePercent").GetDouble().Should().Be(15);
            log.RootElement.GetProperty("actualMinutes").GetDouble().Should().BeApproximately(actual, 0.000001);
            log.RootElement.GetProperty("downloadId").GetString().Should().Be("hash");
            log.RootElement.GetProperty("title").GetString().Should().Be(_movie.Movie.MovieMetadata.Value.Title);
        }

        [Test]
        public void should_catch_odyssey_mismatch()
        {
            _movie.Movie.MovieMetadata.Value.Runtime = 173;
            _movie.MediaInfo.RunTime = TimeSpan.FromSeconds(5163.456);
            Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
            using var log = Observation();
            log.RootElement.GetProperty("wouldHold").GetBoolean().Should().BeTrue();
        }

        [TestCase(false, "Extended", "edition-specific-runtime-unknown")]
        [TestCase(true, null, "multiple-video-files")]
        public void should_record_exempt_comparisons_for_tuning(bool multiple, string edition, string exemption)
        {
            _movie.MediaInfo.RunTime = TimeSpan.FromMinutes(50);
            _movie.OtherVideoFiles = multiple;
            _movie.Edition = edition;
            Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
            using var log = Observation();
            log.RootElement.GetProperty("comparison").GetString().Should().Be("outside");
            log.RootElement.GetProperty("exemption").GetString().Should().Be(exemption);
            log.RootElement.GetProperty("wouldHold").GetBoolean().Should().BeFalse();
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void should_report_unknown_catalog_runtime(int expected)
        {
            _movie.Movie.MovieMetadata.Value.Runtime = expected;
            _movie.MediaInfo.RunTime = TimeSpan.FromMinutes(100);
            Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
            using var log = Observation();
            log.RootElement.GetProperty("comparison").GetString().Should().Be("unavailable");
        }

        [Test]
        public void should_report_missing_probe_without_rejecting()
        {
            _movie.MediaInfo = null;
            Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
            using var log = Observation();
            log.RootElement.GetProperty("comparison").GetString().Should().Be("unavailable");
        }

        [Test]
        public void should_report_unknown_file_context_without_hypothetical_hold()
        {
            _movie.OtherVideoFilesKnown = false;
            _movie.MediaInfo.RunTime = TimeSpan.FromMinutes(50);
            Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
            using var log = Observation();
            log.RootElement.GetProperty("comparison").GetString().Should().Be("outside");
            log.RootElement.GetProperty("multipleVideoFiles").ValueKind.Should().Be(JsonValueKind.Null);
            log.RootElement.GetProperty("exemption").GetString().Should().Be("file-count-unknown");
            log.RootElement.GetProperty("wouldHold").GetBoolean().Should().BeFalse();
        }

        [Test]
        public void should_record_direct_manual_import_observation()
        {
            _movie.MediaInfo.RunTime = TimeSpan.FromMinutes(50);
            _movie.Movie.Path = "/library/Test";
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(42)).Returns(_movie.Movie);
            Mocker.GetMock<IAggregationService>().Setup(s => s.Augment(It.IsAny<LocalMovie>(), null))
                .Returns<LocalMovie, DownloadClientItem>((movie, item) =>
                {
                    movie.MediaInfo = _movie.MediaInfo;
                    return movie;
                });
            Mocker.GetMock<IImportApprovedMovie>()
                .Setup(s => s.Import(It.IsAny<List<ImportDecision>>(), true, null, It.IsAny<ImportMode>()))
                .Returns(new List<ImportResult>());

            Mocker.Resolve<ManualImportService>().Execute(new ManualImportCommand
            {
                Files = new List<ManualImportFile> { new ManualImportFile { MovieId = 42, Path = _movie.Path } }
            });

            using var log = Observation();
            log.RootElement.GetProperty("stage").GetString().Should().Be("manual-import");
            log.RootElement.GetProperty("comparison").GetString().Should().Be("outside");
            Mocker.GetMock<IImportApprovedMovie>()
                .Verify(s => s.Import(It.Is<List<ImportDecision>>(d => d.Single().Approved), true, null, It.IsAny<ImportMode>()), Times.Once());
        }

        [Test]
        public void should_accept_when_logging_target_throws()
        {
            using var target = new ThrowingTarget();
            var rule = new LoggingRule("*", LogLevel.Debug, target);
            var throwExceptions = LogManager.ThrowExceptions;
            LogManager.Configuration.LoggingRules.Add(rule);
            LogManager.ReconfigExistingLoggers();
            try
            {
                LogManager.ThrowExceptions = true;
                Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
                RuntimeReviewSpecification.Observe(LogManager.GetCurrentClassLogger(), _movie, null, null, "manual-import");
                target.Attempts.Should().Be(2);
            }
            finally
            {
                LogManager.ThrowExceptions = throwExceptions;
                LogManager.Configuration.LoggingRules.Remove(rule);
                LogManager.ReconfigExistingLoggers();
            }
        }

        private sealed class ThrowingTarget : Target
        {
            public int Attempts { get; private set; }

            protected override void Write(LogEventInfo logEvent)
            {
                Attempts++;
                throw new InvalidOperationException("Injected logging failure");
            }
        }

        [Test]
        public void should_skip_existing_library_scans()
        {
            _movie.ExistingFile = true;
            Subject.IsSatisfiedBy(_movie, null).Accepted.Should().BeTrue();
            _target.Logs.Should().NotContain(s => s.StartsWith("DevRuntimeReview ", StringComparison.Ordinal));
        }

        private JsonDocument Observation()
        {
            return JsonDocument.Parse(_target.Logs.Single(s => s.StartsWith("DevRuntimeReview ", StringComparison.Ordinal)).Substring("DevRuntimeReview ".Length));
        }
    }
}
