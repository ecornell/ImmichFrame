using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Projects the in-memory settings into the shape that belongs on disk.
/// </summary>
/// <remarks>
/// The runtime object is not a faithful representation of the file:
/// <c>ServerAccountSettings.ValidateAndInitialize()</c> replaces <c>ApiKey</c> with the *contents* of
/// <c>ApiKeyFile</c>. Serializing that directly would write the user's secret into
/// <c>Settings.json</c> in plaintext and produce a file that fails to load next boot ("Cannot specify
/// both ApiKey and ApiKeyFile"). So the key is stripped wherever it came from a file.
/// </remarks>
public static class SettingsFileProjection
{
    public static ServerSettings ToFileModel(ServerSettings live)
    {
        var accounts = (live.AccountsImpl ?? []).Select(account =>
        {
            var copy = ServerSettingsNormalizer.CopyAccount(account);

            if (!string.IsNullOrWhiteSpace(copy.ApiKeyFile))
            {
                // The file stays the source of truth; the resolved secret is never persisted.
                copy.ApiKey = string.Empty;
            }

            return copy;
        }).ToList();

        return new ServerSettings
        {
            GeneralSettingsImpl = live.GeneralSettingsImpl is null
                ? null
                : ServerSettingsNormalizer.CopyGeneral(live.GeneralSettingsImpl),
            AccountsImpl = accounts
        };
    }
}
