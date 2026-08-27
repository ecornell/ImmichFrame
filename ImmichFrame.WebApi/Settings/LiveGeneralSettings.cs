using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Forwards every <see cref="IGeneralSettings"/> member to whatever generation is current, so
/// consumers that hold this instance observe configuration changes without being rebuilt.
/// <para>
/// This is what turns <c>ConfigController</c>, <c>AssetController</c>,
/// <c>ImmichFrameAuthenticationHandler</c> and the per-call reads inside
/// <c>PooledImmichFrameLogic</c> into live consumers with no edits of their own.
/// </para>
/// </summary>
public sealed class LiveGeneralSettings(SettingsStore store) : IGeneralSettings
{
    private IGeneralSettings S => store.Current.Settings.GeneralSettings;

    // --- IClientSettings ---
    public int Interval => S.Interval;
    public double TransitionDuration => S.TransitionDuration;
    public bool DownloadImages => S.DownloadImages;
    public int RenewImagesDuration => S.RenewImagesDuration;
    public bool ShowClock => S.ShowClock;
    public string? ClockFormat => S.ClockFormat;
    public string? ClockDateFormat => S.ClockDateFormat;
    public bool ShowPhotoDate => S.ShowPhotoDate;
    public bool ShowProgressBar => S.ShowProgressBar;
    public string? PhotoDateFormat => S.PhotoDateFormat;
    public bool ShowImageDesc => S.ShowImageDesc;
    public bool ShowPeopleDesc => S.ShowPeopleDesc;
    public bool ShowTagsDesc => S.ShowTagsDesc;
    public bool ShowAlbumName => S.ShowAlbumName;
    public bool ShowImageLocation => S.ShowImageLocation;
    public string? ImageLocationFormat => S.ImageLocationFormat;
    public string? PrimaryColor => S.PrimaryColor;
    public string? SecondaryColor => S.SecondaryColor;
    public string Style => S.Style;
    public string? BaseFontSize => S.BaseFontSize;
    public bool ShowWeatherDescription => S.ShowWeatherDescription;
    public string? WeatherIconUrl => S.WeatherIconUrl;
    public bool ImageZoom => S.ImageZoom;
    public bool ImagePan => S.ImagePan;
    public bool ImageFill => S.ImageFill;
    public bool PlayAudio => S.PlayAudio;
    public string Layout => S.Layout;
    public string Language => S.Language;

    // --- IServerBehaviorSettings ---
    public List<string> Webcalendars => S.Webcalendars;
    public int RefreshAlbumPeopleInterval => S.RefreshAlbumPeopleInterval;
    public string? WeatherApiKey => S.WeatherApiKey;
    public string? WeatherLatLong => S.WeatherLatLong;
    public string? UnitSystem => S.UnitSystem;
    public string? Webhook => S.Webhook;
    public string? AuthenticationSecret => S.AuthenticationSecret;

    /// <summary>
    /// No-op by design. The published generation was already validated before it was swapped in, and
    /// re-validating here would cascade into <c>ServerAccountSettings.ValidateAndInitialize()</c>.
    /// </summary>
    public void Validate() { }
}
