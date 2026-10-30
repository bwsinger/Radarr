using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class NotSampleSpecification : IImportDecisionEngineSpecification
    {
        private readonly IDetectSample _detectSample;
        private readonly IZeroFilledVideoDetector _zeroFilledVideoDetector;
        private readonly Logger _logger;

        public NotSampleSpecification(IDetectSample detectSample,
                                      IZeroFilledVideoDetector zeroFilledVideoDetector,
                                      Logger logger)
        {
            _detectSample = detectSample;
            _zeroFilledVideoDetector = zeroFilledVideoDetector;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            if (localMovie.ExistingFile)
            {
                _logger.Debug("Existing file, skipping sample check");
                return ImportSpecDecision.Accept();
            }

            var sample = _detectSample.IsSample(localMovie.Movie.MovieMetadata, localMovie.Path);

            if (sample == DetectSampleResult.Sample)
            {
                return ImportSpecDecision.Reject(ImportRejectionReason.Sample, "Sample");
            }
            else if (sample == DetectSampleResult.Indeterminate)
            {
                if (downloadClientItem?.Status == DownloadItemStatus.Completed && downloadClientItem.RemainingSize == 0 &&
                    _zeroFilledVideoDetector.IsZeroFilled(localMovie.Path, localMovie.Size))
                {
                    return ImportSpecDecision.Reject(ImportRejectionReason.ZeroFilledVideo, "Video header and sampled data are zero-filled");
                }

                return ImportSpecDecision.Reject(ImportRejectionReason.SampleIndeterminate, "Unable to determine if file is a sample");
            }

            return ImportSpecDecision.Accept();
        }
    }
}
