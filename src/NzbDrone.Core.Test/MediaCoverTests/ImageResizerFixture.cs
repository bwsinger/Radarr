using System;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Test.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace NzbDrone.Core.Test.MediaCoverTests
{
    [TestFixture]
    public class ImageResizerFixture : CoreTest<MediaCover.ImageResizer>
    {
        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.FileExists(It.IsAny<string>()))
                  .Returns<string>(s => File.Exists(s));

            Mocker.GetMock<IDiskProvider>()
                  .Setup(v => v.DeleteFile(It.IsAny<string>()))
                  .Callback<string>(s => File.Delete(s));

            Mocker.SetConstant<IPlatformInfo>(Mocker.Resolve<PlatformInfo>());
        }

        [Test]
        public void should_resize_image()
        {
            var mainFile = Path.Combine(TempFolder, "logo.png");
            var resizedFile = Path.Combine(TempFolder, "logo-170.png");

            File.Copy(GetTestPath("Files/1024.png"), mainFile);

            Subject.Resize(mainFile, resizedFile, 170);

            var fileInfo = new FileInfo(resizedFile);
            fileInfo.Exists.Should().BeTrue();
            fileInfo.Length.Should().BeInRange(1000, 30000);

            using (var image = Image.Load(resizedFile))
            {
                image.Height.Should().Be(170);
                image.Width.Should().Be(170);
            }
        }

        [TestCase("jpg")]
        [TestCase("png")]
        [TestCase("webp")]
        public void should_resize_supported_covers_and_preserve_aspect_ratio(string format)
        {
            var source = Path.Combine(TempFolder, "cover." + format);
            var destination = Path.Combine(TempFolder, "thumbnail.jpg");

            using (var image = new Image<Rgba32>(40, 20))
            {
                image.Save(source);
            }

            Subject.Resize(source, destination, 10);

            using var thumbnail = Image.Load(destination);
            thumbnail.Width.Should().Be(20);
            thumbnail.Height.Should().Be(10);
        }

        [TestCase("tiff")]
        [TestCase("bmp")]
        [TestCase("gif")]
        [TestCase("tga")]
        [TestCase("pbm")]
        public void should_reject_unsupported_formats_even_when_named_as_jpeg(string format)
        {
            var source = Path.Combine(TempFolder, "cover." + format);
            var disguised = Path.Combine(TempFolder, "disguised.jpg");
            var destination = Path.Combine(TempFolder, "thumbnail.jpg");

            using (var image = new Image<Rgba32>(40, 20))
            {
                image.Save(source);
            }

            File.Copy(source, disguised);
            File.WriteAllText(destination, "Previous partial thumbnail");

            Assert.Throws<UnknownImageFormatException>(() => Subject.Resize(disguised, destination, 10));
            File.Exists(destination).Should().BeFalse();

            // The resizer must not change ImageSharp's global decoder configuration.
            using var original = Image.Load(disguised);
            original.Width.Should().Be(40);
        }

        [Test]
        public void should_not_copy_untrusted_metadata_to_thumbnails()
        {
            var source = Path.Combine(TempFolder, "cover.jpg");
            var destination = Path.Combine(TempFolder, "thumbnail.jpg");

            using (var image = new Image<Rgba32>(40, 20))
            {
                image.Metadata.ExifProfile = new ExifProfile();
                image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, "Untrusted cover metadata");
                image.Save(source);
            }

            Subject.Resize(source, destination, 10);

            using var thumbnail = Image.Load(destination);
            thumbnail.Metadata.ExifProfile.Should().BeNull();
        }

        [Test]
        public void should_delete_file_if_failed()
        {
            var mainFile = Path.Combine(TempFolder, "junk.png");
            var resizedFile = Path.Combine(TempFolder, "junk-170.png");

            File.WriteAllText(mainFile, "Just some junk data that should make it throw an Exception.");

            Assert.Throws(Is.InstanceOf<Exception>(), () => Subject.Resize(mainFile, resizedFile, 170));

            File.Exists(resizedFile).Should().BeFalse();
        }
    }
}
