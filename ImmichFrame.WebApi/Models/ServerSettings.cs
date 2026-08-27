using System.Text.Json.Serialization;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using YamlDotNet.Serialization;

namespace ImmichFrame.WebApi.Models;

public class ServerSettings : IServerSettings, IConfigSettable
{
    [YamlMember(Alias = "General")]
    [JsonPropertyName("General")]
    public GeneralSettings? GeneralSettingsImpl { get; set; }

    [YamlMember(Alias = "Accounts")]
    [JsonPropertyName("Accounts")]
    public IEnumerable<ServerAccountSettings> AccountsImpl { get; set; } = new List<ServerAccountSettings>();

    //Covariance not allowed on interface impls
    [JsonIgnore]
    [YamlIgnore]
    public IGeneralSettings GeneralSettings => GeneralSettingsImpl ?? new GeneralSettings();

    [JsonIgnore]
    [YamlIgnore]
    public IEnumerable<IAccountSettings> Accounts => AccountsImpl ?? Enumerable.Empty<ServerAccountSettings>();

    public void Validate()
    {
        GeneralSettings.Validate();

        foreach (var account in Accounts)
        {
            account.ValidateAndInitialize();
        }
    }
}

public class GeneralSettings : IGeneralSettings, IConfigSettable
{
    public bool DownloadImages { get; set; } = false;
    public string Language { get; set; } = "en";
    public string? ImageLocationFormat { get; set; } = "City,State,Country";
    public string? PhotoDateFormat { get; set; } = "MM/dd/yyyy";
    public int Interval { get; set; } = 45;
    public double TransitionDuration { get; set; } = 1;
    public bool ShowClock { get; set; } = true;
    public string? ClockFormat { get; set; } = "hh:mm";
    public string? ClockDateFormat { get; set; } = "eee, MMM d";
    public bool ShowProgressBar { get; set; } = true;
    public bool ShowPhotoDate { get; set; } = true;
    public bool ShowImageDesc { get; set; } = true;
    public bool ShowPeopleDesc { get; set; } = true;
    public bool ShowTagsDesc { get; set; } = true;
    public bool ShowAlbumName { get; set; } = true;
    public bool ShowImageLocation { get; set; } = true;
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string Style { get; set; } = "none";
    public string? BaseFontSize { get; set; }
    public bool ShowWeatherDescription { get; set; } = true;
    public string? WeatherIconUrl { get; set; } = "https://openweathermap.org/img/wn/{IconId}.png";
    public bool ImageZoom { get; set; } = true;
    public bool ImagePan { get; set; } = false;
    public bool ImageFill { get; set; } = false;
    public bool PlayAudio { get; set; } = false;
    public string Layout { get; set; } = "splitview";
    public int RenewImagesDuration { get; set; } = 30;
    public List<string> Webcalendars { get; set; } = new();
    public int RefreshAlbumPeopleInterval { get; set; } = 12;
    public string? WeatherApiKey { get; set; } = string.Empty;
    public string? UnitSystem { get; set; } = "imperial";
    public string? WeatherLatLong { get; set; } = "40.7128,74.0060";
    public string? Webhook { get; set; }
    public string? AuthenticationSecret { get; set; }

    public void Validate() { }
}

public class ServerAccountSettings : IAccountSettings, IConfigSettable
{
    public string ImmichServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? ApiKeyFile { get; set; } = null;
    public bool ShowMemories { get; set; } = false;
    public bool ShowFavorites { get; set; } = false;
    public bool ShowArchived { get; set; } = false;
    public bool ShowVideos { get; set; } = false;

    public int? ImagesFromDays { get; set; }
    public DateTime? ImagesFromDate { get; set; }
    public DateTime? ImagesUntilDate { get; set; }
    public List<Guid> Albums { get; set; } = new();
    public List<Guid> ExcludedAlbums { get; set; } = new();
    public List<Guid> People { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? Rating { get; set; }

    // Private field on purpose: ConfigLoaderTest.VerifyProperties reflects over every public
    // property and asserts an exact value per type, so a public marker would break those tests.
    private bool _apiKeyResolvedFromFile;

    /// <summary>
    /// True when <see cref="ApiKey"/> holds the contents of <see cref="ApiKeyFile"/> rather than a
    /// literal key from the config. The write path uses this to avoid persisting the resolved secret.
    /// </summary>
    [JsonIgnore]
    [YamlIgnore]
    internal bool ApiKeyResolvedFromFile => _apiKeyResolvedFromFile;

    /// <summary>
    /// Carries the resolved-from-file state across a copy. Without this, cloning an account whose key
    /// came from <see cref="ApiKeyFile"/> would produce an object that looks like it has both a
    /// literal key and a key file — which the writer would persist in plaintext and which would fail
    /// validation on the next load.
    /// </summary>
    internal void MarkApiKeyResolvedFromFile() => _apiKeyResolvedFromFile = true;

    public void ValidateAndInitialize()
    {
        // Idempotent: after the first call both ApiKey and ApiKeyFile are populated, so re-running
        // this (which IServerSettings.Validate() does) must not report them as a conflict.
        if (!string.IsNullOrWhiteSpace(ApiKeyFile) && !_apiKeyResolvedFromFile)
        {
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new Exception("Cannot specify both ApiKey and ApiKeyFile. Please provide only one.");
            }
            ApiKey = File.ReadAllText(ApiKeyFile).Trim();
            _apiKeyResolvedFromFile = true;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Either ApiKey or ApiKeyFile must be provided.");
        }
    }
}
