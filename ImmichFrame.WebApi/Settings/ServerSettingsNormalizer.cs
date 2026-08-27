using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Collapses whatever <see cref="IServerSettings"/> shape the loader produced into a concrete
/// <see cref="ServerSettings"/> (the V2 schema), which is the only shape the write path understands.
/// </summary>
/// <remarks>
/// The loader can hand back either a <see cref="ServerSettings"/> (JSON/YAML V2) or a
/// <c>ServerSettingsV1Adapter</c> (legacy JSON/YAML, or environment variables). The adapter also
/// allocates fresh projection objects on every property access, so pinning it down to one concrete
/// instance removes a class of aliasing surprises as well.
/// </remarks>
public static class ServerSettingsNormalizer
{
    public static ServerSettings ToV2(IServerSettings source)
    {
        // Already the right shape: pass through so per-account state (notably whether the API key was
        // resolved from a file) survives untouched.
        if (source is ServerSettings concrete)
        {
            concrete.AccountsImpl ??= new List<ServerAccountSettings>();
            return concrete;
        }

        return new ServerSettings
        {
            GeneralSettingsImpl = CopyGeneral(source.GeneralSettings),
            AccountsImpl = (source.Accounts ?? []).Select(CopyAccount).ToList()
        };
    }

    public static GeneralSettings CopyGeneral(IGeneralSettings g) => new()
    {
        // IClientSettings
        Interval = g.Interval,
        TransitionDuration = g.TransitionDuration,
        DownloadImages = g.DownloadImages,
        RenewImagesDuration = g.RenewImagesDuration,
        ShowClock = g.ShowClock,
        ClockFormat = g.ClockFormat,
        ClockDateFormat = g.ClockDateFormat,
        ShowPhotoDate = g.ShowPhotoDate,
        ShowProgressBar = g.ShowProgressBar,
        PhotoDateFormat = g.PhotoDateFormat,
        ShowImageDesc = g.ShowImageDesc,
        ShowPeopleDesc = g.ShowPeopleDesc,
        ShowTagsDesc = g.ShowTagsDesc,
        ShowAlbumName = g.ShowAlbumName,
        ShowImageLocation = g.ShowImageLocation,
        ImageLocationFormat = g.ImageLocationFormat,
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
        Language = g.Language,

        // IServerBehaviorSettings
        Webcalendars = [.. g.Webcalendars],
        RefreshAlbumPeopleInterval = g.RefreshAlbumPeopleInterval,
        WeatherApiKey = g.WeatherApiKey,
        WeatherLatLong = g.WeatherLatLong,
        UnitSystem = g.UnitSystem,
        Webhook = g.Webhook,
        AuthenticationSecret = g.AuthenticationSecret
    };

    public static ServerAccountSettings CopyAccount(IAccountSettings a)
    {
        var copy = new ServerAccountSettings
        {
            ImmichServerUrl = a.ImmichServerUrl,
            ApiKey = a.ApiKey,
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

        // Preserve the source's own flag when copying a concrete account; otherwise infer it, since a
        // loaded account holding both a key and a key file can only have got there by resolution.
        var resolvedFromFile = a is ServerAccountSettings src
            ? src.ApiKeyResolvedFromFile
            : !string.IsNullOrWhiteSpace(a.ApiKeyFile) && !string.IsNullOrWhiteSpace(a.ApiKey);

        if (resolvedFromFile) copy.MarkApiKeyResolvedFromFile();

        return copy;
    }
}
