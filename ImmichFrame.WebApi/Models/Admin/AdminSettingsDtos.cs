namespace ImmichFrame.WebApi.Models.Admin;

/// <summary>
/// The editable General settings.
/// </summary>
/// <remarks>
/// Every property carries the same default as <see cref="GeneralSettings"/>. That is load-bearing:
/// a field the client omits would otherwise deserialize to <c>0</c>/<c>null</c> and quietly wipe a
/// working setting.
/// <para>
/// <c>AuthenticationSecret</c> is deliberately absent in both directions — it stays file-only so a
/// bad save can never lock you out of the very page you would fix it from.
/// </para>
/// </remarks>
public class AdminGeneralSettingsDto
{
    // --- client-visible ---
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

    // --- server behavior ---
    public List<string> Webcalendars { get; set; } = new();
    public int RefreshAlbumPeopleInterval { get; set; } = 12;
    public string? WeatherApiKey { get; set; } = string.Empty;
    public string? UnitSystem { get; set; } = "imperial";
    public string? WeatherLatLong { get; set; } = "40.7128,74.0060";
    public string? Webhook { get; set; }
}

/// <summary>
/// An account as shown to the browser. Note the absence of any API key property.
/// </summary>
public class AdminAccountViewDto
{
    public int Index { get; set; }
    public string ImmichServerUrl { get; set; } = string.Empty;

    /// <summary>Whether a key is configured, without revealing it.</summary>
    public bool ApiKeyIsSet { get; set; }

    /// <summary><c>inline</c>, <c>file</c>, or <c>none</c>.</summary>
    public string ApiKeySource { get; set; } = "none";

    public string? ApiKeyFile { get; set; }
    public bool ShowMemories { get; set; }
    public bool ShowFavorites { get; set; }
    public bool ShowArchived { get; set; }
    public bool ShowVideos { get; set; }
    public int? ImagesFromDays { get; set; }
    public DateTime? ImagesFromDate { get; set; }
    public DateTime? ImagesUntilDate { get; set; }
    public List<Guid> Albums { get; set; } = new();
    public List<Guid> ExcludedAlbums { get; set; } = new();
    public List<Guid> People { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? Rating { get; set; }
}

public class AdminSettingsViewDto
{
    /// <summary>Echo this back when saving; a mismatch means someone else saved in between.</summary>
    public long Version { get; set; }

    public string ConfigFilePath { get; set; } = string.Empty;

    /// <summary>False when the config directory is not writable (common in the stock container).</summary>
    public bool CanPersist { get; set; }

    public List<string> Warnings { get; set; } = new();
    public AdminGeneralSettingsDto General { get; set; } = new();
    public List<AdminAccountViewDto> Accounts { get; set; } = new();
}

/// <summary>
/// An incoming account. <see cref="ApiKey"/> is write-only and uses presence semantics.
/// </summary>
public class AdminAccountUpdateDto
{
    /// <summary>Position in the configuration the client loaded; <c>null</c> for a new account.</summary>
    public int? Index { get; set; }

    public string ImmichServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Omitted or <c>null</c> keeps the existing key; empty clears it; any other value replaces it.
    /// Detecting a mask sentinel was rejected deliberately — a user could legitimately have one as
    /// their key, and the mask belongs to the UI, not the wire format.
    /// </summary>
    public string? ApiKey { get; set; }

    public string? ApiKeyFile { get; set; }
    public bool ShowMemories { get; set; }
    public bool ShowFavorites { get; set; }
    public bool ShowArchived { get; set; }
    public bool ShowVideos { get; set; }
    public int? ImagesFromDays { get; set; }
    public DateTime? ImagesFromDate { get; set; }
    public DateTime? ImagesUntilDate { get; set; }
    public List<Guid> Albums { get; set; } = new();
    public List<Guid> ExcludedAlbums { get; set; } = new();
    public List<Guid> People { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? Rating { get; set; }
}

public class AdminSettingsUpdateDto
{
    public long Version { get; set; }

    /// <summary>Save even though one or more Immich servers failed the connectivity probe.</summary>
    public bool Force { get; set; }

    public AdminGeneralSettingsDto General { get; set; } = new();
    public List<AdminAccountUpdateDto> Accounts { get; set; } = new();
}

/// <summary>
/// Asks the server to list one account's albums, people, and tags.
/// </summary>
/// <remarks>
/// Takes the account position plus the URL/key currently typed into the form, so browsing works for
/// an account that has not been saved yet. <c>ApiKeyFile</c> is deliberately absent: reading it here
/// would turn a browse into an arbitrary server-side file read, and a saved account's key is already
/// resolved in memory anyway.
/// </remarks>
public class AdminBrowseRequestDto
{
    /// <summary>Position in the loaded configuration; <c>null</c> for an account being added.</summary>
    public int? Index { get; set; }

    public string ImmichServerUrl { get; set; } = string.Empty;

    /// <summary>Omitted uses the saved account's key; a value browses with that key instead.</summary>
    public string? ApiKey { get; set; }
}

public class CatalogEntryDto
{
    /// <summary>What the config file stores: a GUID for albums and people, the value for tags.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Asset count where the server reports one.</summary>
    public long? Count { get; set; }
}

public class AdminBrowseResultDto
{
    public bool Reachable { get; set; }
    public string? Error { get; set; }
    public List<CatalogEntryDto> Albums { get; set; } = new();
    public List<CatalogEntryDto> People { get; set; } = new();
    public List<CatalogEntryDto> Tags { get; set; } = new();
}

public class AccountProbeResultDto
{
    public int Index { get; set; }
    public string ImmichServerUrl { get; set; } = string.Empty;
    public bool Reachable { get; set; }
    public string? ServerVersion { get; set; }
    public bool VersionSupported { get; set; }
    public string? Error { get; set; }
}

public class SaveSettingsResultDto
{
    public bool Applied { get; set; }
    public long Version { get; set; }
    public List<string> Problems { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<AccountProbeResultDto> AccountResults { get; set; } = new();
}
