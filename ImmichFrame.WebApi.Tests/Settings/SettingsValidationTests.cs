using System.Globalization;
using AwesomeAssertions;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Settings;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Settings;

[TestFixture]
public class SettingsValidationTests
{
    private static ServerSettings With(GeneralSettings general) => new()
    {
        GeneralSettingsImpl = general,
        AccountsImpl = [new ServerAccountSettings { ImmichServerUrl = "http://immich.example", ApiKey = "k" }]
    };

    [Test]
    public void Defaults_AreValid()
    {
        SettingsValidator.TryValidate(With(new GeneralSettings()), out var problems).Should().BeTrue(string.Join("; ", problems));
    }

    [Test]
    public void DefaultWeatherLocation_IsNewYorkCity()
    {
        // Longitude west of Greenwich is negative; the old default pointed at western China.
        new GeneralSettings().WeatherLatLong.Should().Be("40.7128,-74.0060");
        new ServerSettingsV1().WeatherLatLong.Should().Be("40.7128,-74.0060");
    }

    [TestCase("not a language!")]
    [TestCase("en_US!!")]
    public void Validator_RejectsUnknownLanguage(string language)
    {
        SettingsValidator.TryValidate(With(new GeneralSettings { Language = language }), out var problems).Should().BeFalse();
        problems.Should().Contain(p => p.Contains("Language"));
    }

    [TestCase("en")]
    [TestCase("de-DE")]
    [TestCase("")]
    public void Validator_AcceptsKnownOrEmptyLanguage(string language)
    {
        SettingsValidator.TryValidate(With(new GeneralSettings { Language = language }), out _).Should().BeTrue();
    }

    [Test]
    public void Validator_RejectsInvalidPhotoDateFormat()
    {
        SettingsValidator.TryValidate(With(new GeneralSettings { PhotoDateFormat = "%" }), out var problems).Should().BeFalse();
        problems.Should().Contain(p => p.Contains("PhotoDateFormat"));
    }

    [Test]
    public void FormatPhotoDate_BadSettings_FallBackInsteadOfThrowing()
    {
        var date = new DateTimeOffset(2024, 3, 9, 0, 0, 0, TimeSpan.Zero);

        AssetDateFormatting.FormatPhotoDate(date, null, "en").Should().Be("03/09/2024");
        AssetDateFormatting.FormatPhotoDate(date, "%", "en").Should().Be("03/09/2024");
        AssetDateFormatting.FormatPhotoDate(date, "yyyy", "not a language!").Should().Be("2024");
    }

    [Test]
    public void FormatPhotoDate_KeepsEscapedQuotes()
    {
        var date = new DateTimeOffset(2024, 3, 9, 0, 0, 0, TimeSpan.Zero);

        AssetDateFormatting.FormatPhotoDate(date, "yyyy''MM", "en").Should().Be("2024'03");
    }

    [Test]
    public void WeatherLatLong_IsParsedWithTheInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            OpenWeatherMapService.TryParseLatLong("40.7128,-74.0060", out var lat, out var lon).Should().BeTrue();
            lat.Should().BeApproximately(40.7128, 1e-9);
            lon.Should().BeApproximately(-74.0060, 1e-9);

            OpenWeatherMapService.TryParseLatLong("garbage", out _, out _).Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
