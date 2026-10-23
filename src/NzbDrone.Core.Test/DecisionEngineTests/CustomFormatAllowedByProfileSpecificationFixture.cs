using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]

    public class CustomFormatAllowedByProfileSpecificationFixture : CoreTest<CustomFormatAllowedbyProfileSpecification>
    {
        private RemoteMovie _remoteMovie;

        private CustomFormat _format1;
        private CustomFormat _format2;

        [SetUp]
        public void Setup()
        {
            _format1 = new CustomFormat("Awesome Format");
            _format1.Id = 1;

            _format2 = new CustomFormat("Cool Format");
            _format2.Id = 2;

            var fakeSeries = Builder<Movie>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    Cutoff = Quality.Bluray1080p.Id,
                    MinFormatScore = 1
                })
                .Build();

            _remoteMovie = new RemoteMovie
            {
                Movie = fakeSeries,
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(Quality.DVD, new Revision(version: 2)) },
            };

            CustomFormatsTestHelpers.GivenCustomFormats(_format1, _format2);
        }

        [Test]
        public void should_allow_if_format_score_greater_than_min()
        {
            _remoteMovie.CustomFormats = new List<CustomFormat> { _format1 };
            _remoteMovie.Movie.QualityProfile.FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems(_format1.Name);
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);

            Subject.IsSatisfiedBy(_remoteMovie, new()).Accepted.Should().BeTrue();
        }

        [TestCase("Годзилла: Минус один / Gojira -1.0 / Godzilla: Minus One (2023) BDRip [H.264/1080p-LQ]", "LQ", false, 0, true)]
        [TestCase("05 - Годзилла. Минус один (2023).mkv", null, false, -10000, false)]
        [TestCase("Годзилла: Минус один / Gojira -1.0 / Godzilla: Minus One (2023) BDRip [H.264/1080p-LQ]", "LQ", true, -10000, false)]
        [TestCase("Godzilla Minus One 2023 1080p BluRay-LQTeam", "LQTeam", true, 0, true)]
        public void should_enforce_minimum_against_the_group_known_at_each_stage(string title, string group, bool includeLqRule, int score, bool accepted)
        {
            var formats = new List<CustomFormat> { new CustomFormat("No-RlsGroup", new ReleaseGroupSpecification { Value = ".", Negate = true }) { Id = 12 } };
            if (includeLqRule)
            {
                formats.Add(new CustomFormat("LQ", new ReleaseGroupSpecification { Value = "^YTS$" }, new ReleaseGroupSpecification { Value = "^LQ$" }) { Id = 10 });
            }

            Mocker.GetMock<ICustomFormatService>().Setup(s => s.All()).Returns(formats);

            _remoteMovie.Movie.MovieFileId = 0;
            _remoteMovie.Movie.QualityProfile.MinFormatScore = 0;
            _remoteMovie.Movie.QualityProfile.Items = Qualities.QualityFixture.GetDefaultQualities();
            _remoteMovie.Movie.QualityProfile.FormatItems = formats.ConvertAll(format => new ProfileFormatItem { Format = format, Score = -10000 });
            _remoteMovie.ParsedMovieInfo = Parser.Parser.ParseMovieTitle(title);
            _remoteMovie.ParsedMovieInfo.Should().NotBeNull();
            _remoteMovie.ParsedMovieInfo.ReleaseGroup.Should().Be(group);

            var calculator = Mocker.Resolve<CustomFormatCalculationService>();
            _remoteMovie.CustomFormats = calculator.ParseCustomFormat(_remoteMovie, 5682987705);
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);
            _remoteMovie.CustomFormatScore.Should().Be(score);
            var grabDecision = Subject.IsSatisfiedBy(_remoteMovie, new());
            grabDecision.Accepted.Should().Be(accepted);

            var localMovie = new LocalMovie
            {
                Movie = _remoteMovie.Movie,
                Path = title,
                Quality = _remoteMovie.ParsedMovieInfo.Quality,
                DownloadClientMovieInfo = _remoteMovie.ParsedMovieInfo,
                FileMovieInfo = Parser.Parser.ParseMoviePath(title)
            };
            new AggregateReleaseGroup().Aggregate(localMovie, null);
            localMovie.ReleaseGroup.Should().Be(group);
            localMovie.CustomFormats = calculator.ParseCustomFormat(localMovie);
            localMovie.CustomFormatScore = localMovie.Movie.QualityProfile.CalculateCustomFormatScore(localMovie.CustomFormats);
            localMovie.CustomFormatScore.Should().Be(score);
            var importDecision = Mocker.Resolve<NzbDrone.Core.MediaFiles.MovieImport.Specifications.UpgradeSpecification>().IsSatisfiedBy(localMovie, null);
            importDecision.Accepted.Should().Be(accepted);
            if (!accepted)
            {
                grabDecision.Reason.Should().Be(DownloadRejectionReason.CustomFormatMinimumScore);
                importDecision.Reason.Should().Be(ImportRejectionReason.BelowMinimumCustomFormatScore);
            }
        }

        [Test]
        public void should_deny_if_format_score_not_greater_than_min()
        {
            _remoteMovie.CustomFormats = new List<CustomFormat> { _format2 };
            _remoteMovie.Movie.QualityProfile.FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems(_format1.Name);
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);

            Console.WriteLine(_remoteMovie.CustomFormatScore);
            Console.WriteLine(_remoteMovie.Movie.QualityProfile.MinFormatScore);

            Subject.IsSatisfiedBy(_remoteMovie, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_deny_if_format_score_not_greater_than_min_2()
        {
            _remoteMovie.CustomFormats = new List<CustomFormat> { _format2, _format1 };
            _remoteMovie.Movie.QualityProfile.FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems(_format1.Name);
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);

            Subject.IsSatisfiedBy(_remoteMovie, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_allow_if_all_format_is_defined_in_profile()
        {
            _remoteMovie.CustomFormats = new List<CustomFormat> { _format2, _format1 };
            _remoteMovie.Movie.QualityProfile.FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems(_format1.Name, _format2.Name);
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);

            Subject.IsSatisfiedBy(_remoteMovie, new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_deny_if_no_format_was_parsed_and_min_score_positive()
        {
            _remoteMovie.CustomFormats = new List<CustomFormat> { };
            _remoteMovie.Movie.QualityProfile.FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems(_format1.Name, _format2.Name);
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);

            Subject.IsSatisfiedBy(_remoteMovie, new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_allow_if_no_format_was_parsed_min_score_is_zero()
        {
            _remoteMovie.CustomFormats = new List<CustomFormat> { };
            _remoteMovie.Movie.QualityProfile.FormatItems = CustomFormatsTestHelpers.GetSampleFormatItems(_format1.Name, _format2.Name);
            _remoteMovie.Movie.QualityProfile.MinFormatScore = 0;
            _remoteMovie.CustomFormatScore = _remoteMovie.Movie.QualityProfile.CalculateCustomFormatScore(_remoteMovie.CustomFormats);

            Subject.IsSatisfiedBy(_remoteMovie, new()).Accepted.Should().BeTrue();
        }
    }
}
