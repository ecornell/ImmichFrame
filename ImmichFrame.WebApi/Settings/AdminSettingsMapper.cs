using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Models.Admin;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Translates between the wire DTOs and <see cref="ServerSettings"/>.
/// </summary>
public static class AdminSettingsMapper
{
    /// <summary>
    /// Builds a brand-new <see cref="ServerSettings"/> from the request. The current settings are
    /// only read, never mutated, so a rejected save leaves the running configuration untouched.
    /// </summary>
    public static ServerSettings Apply(AdminSettingsUpdateDto dto, ServerSettings current)
    {
        var currentAccounts = (current.AccountsImpl ?? []).ToList();

        var general = new GeneralSettings
        {
            DownloadImages = dto.General.DownloadImages,
            Language = dto.General.Language,
            ImageLocationFormat = dto.General.ImageLocationFormat,
            PhotoDateFormat = dto.General.PhotoDateFormat,
            Interval = dto.General.Interval,
            TransitionDuration = dto.General.TransitionDuration,
            ShowClock = dto.General.ShowClock,
            ClockFormat = dto.General.ClockFormat,
            ClockDateFormat = dto.General.ClockDateFormat,
            ShowProgressBar = dto.General.ShowProgressBar,
            ShowPhotoDate = dto.General.ShowPhotoDate,
            ShowImageDesc = dto.General.ShowImageDesc,
            ShowPeopleDesc = dto.General.ShowPeopleDesc,
            ShowTagsDesc = dto.General.ShowTagsDesc,
            ShowAlbumName = dto.General.ShowAlbumName,
            ShowImageLocation = dto.General.ShowImageLocation,
            PrimaryColor = dto.General.PrimaryColor,
            SecondaryColor = dto.General.SecondaryColor,
            Style = dto.General.Style,
            BaseFontSize = dto.General.BaseFontSize,
            ShowWeatherDescription = dto.General.ShowWeatherDescription,
            WeatherIconUrl = dto.General.WeatherIconUrl,
            ImageZoom = dto.General.ImageZoom,
            ImagePan = dto.General.ImagePan,
            ImageFill = dto.General.ImageFill,
            PlayAudio = dto.General.PlayAudio,
            Layout = dto.General.Layout,
            RenewImagesDuration = dto.General.RenewImagesDuration,
            Webcalendars = [.. dto.General.Webcalendars],
            RefreshAlbumPeopleInterval = dto.General.RefreshAlbumPeopleInterval,
            WeatherApiKey = dto.General.WeatherApiKey,
            UnitSystem = dto.General.UnitSystem,
            WeatherLatLong = dto.General.WeatherLatLong,
            Webhook = dto.General.Webhook,

            // Not part of the DTO in either direction. Carrying it forward explicitly is what stops a
            // save from silently disabling authentication for the whole app.
            AuthenticationSecret = current.GeneralSettingsImpl?.AuthenticationSecret
        };

        var accounts = dto.Accounts.Select(a => MapAccount(a, currentAccounts)).ToList();

        return new ServerSettings
        {
            GeneralSettingsImpl = general,
            AccountsImpl = accounts
        };
    }

    private static ServerAccountSettings MapAccount(
        AdminAccountUpdateDto dto, IReadOnlyList<ServerAccountSettings> currentAccounts)
    {
        var existing = dto.Index is int i && i >= 0 && i < currentAccounts.Count
            ? currentAccounts[i]
            : null;

        var account = new ServerAccountSettings
        {
            ImmichServerUrl = dto.ImmichServerUrl,
            ShowMemories = dto.ShowMemories,
            ShowFavorites = dto.ShowFavorites,
            ShowArchived = dto.ShowArchived,
            ShowVideos = dto.ShowVideos,
            ImagesFromDays = dto.ImagesFromDays,
            ImagesFromDate = dto.ImagesFromDate,
            ImagesUntilDate = dto.ImagesUntilDate,
            Albums = [.. dto.Albums],
            ExcludedAlbums = [.. dto.ExcludedAlbums],
            People = [.. dto.People],
            Tags = [.. dto.Tags],
            Rating = dto.Rating
        };

        if (dto.ApiKey is null)
        {
            // Untouched by the form: keep whatever the account already had, including the
            // resolved-from-file state so the writer still knows not to persist the secret.
            account.ApiKey = existing?.ApiKey ?? string.Empty;
            account.ApiKeyFile = dto.ApiKeyFile ?? existing?.ApiKeyFile;

            if (existing?.ApiKeyResolvedFromFile == true &&
                string.Equals(account.ApiKeyFile, existing.ApiKeyFile, StringComparison.Ordinal))
            {
                account.MarkApiKeyResolvedFromFile();
            }
        }
        else if (dto.ApiKey.Length == 0)
        {
            // Explicitly cleared — only valid alongside an ApiKeyFile, which the validator enforces.
            account.ApiKey = string.Empty;
            account.ApiKeyFile = dto.ApiKeyFile;
        }
        else
        {
            // A literal key replaces any file indirection, otherwise the next load would reject the
            // file for specifying both.
            account.ApiKey = dto.ApiKey;
            account.ApiKeyFile = null;
        }

        return account;
    }

    public static AdminSettingsViewDto ToView(SettingsGeneration generation, ConfigLocation location, List<string> warnings)
    {
        var settings = generation.Settings;
        var general = settings.GeneralSettingsImpl ?? new GeneralSettings();

        return new AdminSettingsViewDto
        {
            Version = generation.Version,
            ConfigFilePath = location.SettingsJsonPath,
            CanPersist = location.CanPersist(),
            Warnings = warnings,
            General = ToGeneralView(general),
            Accounts = (settings.AccountsImpl ?? []).Select((a, i) => ToAccountView(a, i)).ToList()
        };
    }

    private static AdminGeneralSettingsDto ToGeneralView(GeneralSettings g) => new()
    {
        DownloadImages = g.DownloadImages,
        Language = g.Language,
        ImageLocationFormat = g.ImageLocationFormat,
        PhotoDateFormat = g.PhotoDateFormat,
        Interval = g.Interval,
        TransitionDuration = g.TransitionDuration,
        ShowClock = g.ShowClock,
        ClockFormat = g.ClockFormat,
        ClockDateFormat = g.ClockDateFormat,
        ShowProgressBar = g.ShowProgressBar,
        ShowPhotoDate = g.ShowPhotoDate,
        ShowImageDesc = g.ShowImageDesc,
        ShowPeopleDesc = g.ShowPeopleDesc,
        ShowTagsDesc = g.ShowTagsDesc,
        ShowAlbumName = g.ShowAlbumName,
        ShowImageLocation = g.ShowImageLocation,
        PrimaryColor = g.PrimaryColor,
        SecondaryColor = g.SecondaryColor,
        Style = g.Style,
        BaseFontSize = g.BaseFontSize,
        ShowWeatherDescription = g.ShowWeatherDescription,
        WeatherIconUrl = g.WeatherIconUrl,
        ImageZoom = g.ImageZoom,
        ImagePan = g.ImagePan,
        ImageFill = g.ImageFill,
        PlayAudio = g.PlayAudio,
        Layout = g.Layout,
        RenewImagesDuration = g.RenewImagesDuration,
        Webcalendars = [.. g.Webcalendars],
        RefreshAlbumPeopleInterval = g.RefreshAlbumPeopleInterval,
        WeatherApiKey = g.WeatherApiKey,
        UnitSystem = g.UnitSystem,
        WeatherLatLong = g.WeatherLatLong,
        Webhook = g.Webhook
        // AuthenticationSecret intentionally omitted.
    };

    public static AdminAccountViewDto ToAccountView(ServerAccountSettings a, int index) => new()
    {
        Index = index,
        ImmichServerUrl = a.ImmichServerUrl,
        ApiKeyIsSet = !string.IsNullOrWhiteSpace(a.ApiKey) || !string.IsNullOrWhiteSpace(a.ApiKeyFile),
        ApiKeySource = !string.IsNullOrWhiteSpace(a.ApiKeyFile)
            ? "file"
            : !string.IsNullOrWhiteSpace(a.ApiKey) ? "inline" : "none",
        ApiKeyFile = a.ApiKeyFile,
        ShowMemories = a.ShowMemories,
        ShowFavorites = a.ShowFavorites,
        ShowArchived = a.ShowArchived,
        ShowVideos = a.ShowVideos,
        ImagesFromDays = a.ImagesFromDays,
        ImagesFromDate = a.ImagesFromDate,
        ImagesUntilDate = a.ImagesUntilDate,
        Albums = [.. a.Albums],
        ExcludedAlbums = [.. a.ExcludedAlbums],
        People = [.. a.People],
        Tags = [.. a.Tags],
        Rating = a.Rating
    };
}
