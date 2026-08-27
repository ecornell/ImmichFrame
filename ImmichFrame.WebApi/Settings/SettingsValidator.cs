using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Structural validation of a candidate <see cref="ServerSettings"/>.
/// <para>
/// Deliberately separate from <see cref="ServerSettings.Validate"/>: this variant never mutates the
/// object it inspects (in particular it does not resolve <c>ApiKeyFile</c> into <c>ApiKey</c>) and
/// collects every problem instead of throwing on the first, so a form can show them all at once.
/// </para>
/// </summary>
public static class SettingsValidator
{
    private static readonly string[] KnownStyles = ["none", "solid", "transition", "blur"];
    private static readonly string[] KnownLayouts = ["single", "splitview"];

    public static bool TryValidate(ServerSettings candidate, out List<string> problems)
    {
        problems = [];

        ValidateGeneral(candidate.GeneralSettingsImpl, problems);

        var accounts = (candidate.AccountsImpl ?? []).ToList();
        if (accounts.Count == 0)
        {
            problems.Add("At least one Immich account is required.");
        }

        for (var i = 0; i < accounts.Count; i++)
        {
            ValidateAccount(accounts[i], i, problems);
        }

        return problems.Count == 0;
    }

    private static void ValidateGeneral(GeneralSettings? general, List<string> problems)
    {
        if (general is null) return;

        if (general.Interval <= 0)
            problems.Add("Interval must be greater than 0 seconds.");

        if (general.TransitionDuration < 0)
            problems.Add("TransitionDuration cannot be negative.");

        if (general.RenewImagesDuration < 0)
            problems.Add("RenewImagesDuration cannot be negative.");

        if (general.RefreshAlbumPeopleInterval < 0)
            problems.Add("RefreshAlbumPeopleInterval cannot be negative.");

        if (!string.IsNullOrWhiteSpace(general.Style) &&
            !KnownStyles.Contains(general.Style, StringComparer.OrdinalIgnoreCase))
            problems.Add($"Style '{general.Style}' is not one of: {string.Join(", ", KnownStyles)}.");

        if (!string.IsNullOrWhiteSpace(general.Layout) &&
            !KnownLayouts.Contains(general.Layout, StringComparer.OrdinalIgnoreCase))
            problems.Add($"Layout '{general.Layout}' is not one of: {string.Join(", ", KnownLayouts)}.");

        // Only meaningful when weather is actually switched on.
        if (!string.IsNullOrWhiteSpace(general.WeatherApiKey) && !IsValidLatLong(general.WeatherLatLong))
            problems.Add("WeatherLatLong must be two comma-separated numbers, e.g. '40.7128,-74.0060'.");

        foreach (var url in general.Webcalendars.Where(u => !IsAbsoluteHttpUrl(u)))
            problems.Add($"Webcalendar '{url}' is not a valid http(s) URL.");
    }

    private static void ValidateAccount(ServerAccountSettings account, int index, List<string> problems)
    {
        var label = $"Account {index + 1}";

        if (string.IsNullOrWhiteSpace(account.ImmichServerUrl))
        {
            problems.Add($"{label}: ImmichServerUrl is required.");
        }
        else if (!IsAbsoluteHttpUrl(account.ImmichServerUrl))
        {
            problems.Add($"{label}: ImmichServerUrl '{account.ImmichServerUrl}' is not a valid http(s) URL.");
        }

        var hasKey = !string.IsNullOrWhiteSpace(account.ApiKey);
        var hasKeyFile = !string.IsNullOrWhiteSpace(account.ApiKeyFile);

        // A key already resolved from a file is not a conflict — it is the expected post-load state.
        if (hasKey && hasKeyFile && !account.ApiKeyResolvedFromFile)
        {
            problems.Add($"{label}: specify either ApiKey or ApiKeyFile, not both.");
        }
        else if (!hasKey && !hasKeyFile)
        {
            problems.Add($"{label}: either ApiKey or ApiKeyFile must be provided.");
        }

        if (hasKeyFile && !File.Exists(account.ApiKeyFile))
        {
            problems.Add($"{label}: ApiKeyFile '{account.ApiKeyFile}' does not exist or is not readable.");
        }

        // Immich ratings run -1..5 (see docs/docs/getting-started/configuration.md).
        if (account.Rating is < -1 or > 5)
        {
            problems.Add($"{label}: Rating must be between -1 and 5.");
        }

        if (account.ImagesFromDate.HasValue && account.ImagesUntilDate.HasValue &&
            account.ImagesFromDate > account.ImagesUntilDate)
        {
            problems.Add($"{label}: ImagesFromDate must not be later than ImagesUntilDate.");
        }
    }

    private static bool IsAbsoluteHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsValidLatLong(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(',');
        return parts.Length == 2
               && double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out _)
               && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
