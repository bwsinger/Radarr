using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;

namespace NzbDrone.Core.MediaFiles
{
    public interface IZeroFilledVideoDetector
    {
        bool IsZeroFilled(string path, long expectedSize);
    }

    public class ZeroFilledVideoDetector : IZeroFilledVideoDetector
    {
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public ZeroFilledVideoDetector(IDiskProvider diskProvider, Logger logger)
        {
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public bool IsZeroFilled(string path, long expectedSize)
        {
            var extension = Path.GetExtension(path)?.ToLowerInvariant();
            if (expectedSize < 1024 * 1024 || (extension != ".mkv" && extension != ".mp4" && extension != ".m4v"))
            {
                return false;
            }

            try
            {
                var lastWrite = _diskProvider.FileGetLastWrite(path);
                using var stream = _diskProvider.OpenReadStream(path);
                if (stream.Length != expectedSize)
                {
                    return false;
                }

                // These containers require a nonzero header. Check distant data too,
                // and leave short reads or changing files for manual review.
                var buffer = new byte[64 * 1024];
                foreach (var offset in new[] { 0, expectedSize / 2, expectedSize - buffer.Length })
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    var read = 0;
                    while (read < buffer.Length)
                    {
                        var count = stream.Read(buffer, read, buffer.Length - read);
                        if (count == 0)
                        {
                            return false;
                        }

                        read += count;
                    }

                    if (Array.Exists(buffer, value => value != 0))
                    {
                        return false;
                    }
                }

                return stream.Length == expectedSize && _diskProvider.FileGetLastWrite(path) == lastWrite;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                _logger.Debug(ex, "Cannot verify zero-filled video: {0}", path);
                return false;
            }
        }
    }
}
