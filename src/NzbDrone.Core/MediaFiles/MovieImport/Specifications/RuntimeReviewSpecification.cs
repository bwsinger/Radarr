using System;
using System.Text.Json;
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class RuntimeReviewSpecification : IImportDecisionEngineSpecification
    {
        // ponytail: catalog runtime is edition-agnostic; tune from reviewed observations before enforcing.
        private const double TolerancePercent = 15;
        private readonly Logger _logger;

        public RuntimeReviewSpecification(Logger logger)
        {
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            Observe(_logger, localMovie, downloadClientItem, localMovie.OtherVideoFilesKnown ? localMovie.OtherVideoFiles : null, "import-decision");
            return ImportSpecDecision.Accept();
        }

        public static void Observe(Logger logger, LocalMovie localMovie, DownloadClientItem downloadClientItem, bool? multipleVideoFiles, string stage)
        {
            try
            {
                LogObservation(logger, localMovie, downloadClientItem, multipleVideoFiles, stage);
            }
            catch (Exception)
            {
                // A throwing logging target cannot safely log its own failure. Observation never blocks import.
            }
        }

        private static void LogObservation(Logger logger, LocalMovie localMovie, DownloadClientItem downloadClientItem, bool? multipleVideoFiles, string stage)
        {
            if (localMovie.ExistingFile)
            {
                return;
            }

            var metadata = localMovie.Movie?.MovieMetadata?.Value;
            var expected = metadata?.Runtime ?? 0;
            var actual = localMovie.MediaInfo?.RunTime.TotalMinutes ?? 0;
            double? deviationPercent = expected > 0 && actual > 0 ? (actual - expected) / expected * 100 : null;
            var outside = deviationPercent.HasValue && Math.Abs(deviationPercent.Value) > TolerancePercent;
            var exemption = !multipleVideoFiles.HasValue ? "file-count-unknown" :
                multipleVideoFiles.Value ? "multiple-video-files" :
                !string.IsNullOrWhiteSpace(localMovie.Edition) ? "edition-specific-runtime-unknown" : null;

            // Info is retained by normal Radarr logs; JSON keeps titles/paths on one parseable line.
            logger.Info("DevRuntimeReview {0}", JsonSerializer.Serialize(new
            {
                version = 1,
                mode = "observe",
                stage,
                utc = DateTime.UtcNow,
                movieId = localMovie.Movie?.Id,
                tmdbId = metadata?.TmdbId,
                imdbId = metadata?.ImdbId,
                title = metadata?.Title,
                year = metadata?.Year,
                downloadId = downloadClientItem?.DownloadId,
                downloadTitle = downloadClientItem?.Title,
                path = localMovie.Path,
                edition = localMovie.Edition,
                multipleVideoFiles,
                expectedMinutes = expected,
                actualMinutes = actual,
                deviationPercent,
                tolerancePercent = TolerancePercent,
                comparison = !deviationPercent.HasValue ? "unavailable" : outside ? "outside" : "within",
                exemption,
                wouldHold = outside && exemption == null
            }));
        }
    }
}
