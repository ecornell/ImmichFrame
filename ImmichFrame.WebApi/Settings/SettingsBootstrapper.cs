using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Publishes generation 1 of the settings at startup.
/// </summary>
/// <remarks>
/// The seed comes from the registered <see cref="IServerSettings"/> rather than from
/// <c>ConfigLoader</c> directly. That indirection is deliberate: the WebApi test fixtures replace
/// that registration via <c>ConfigureTestServices</c>, and calling the loader here would make them
/// fail at host startup with "Failed to load configuration".
/// </remarks>
public sealed class SettingsBootstrapper(
    SettingsStore store,
    IServerSettings seed,
    ImmichFrameLogicFactory logicFactory,
    ILogger<SettingsBootstrapper> logger)
{
    public void Initialize()
    {
        if (store.IsInitialized) return;

        var settings = ServerSettingsNormalizer.ToV2(seed);

        store.Initialize(new SettingsGeneration
        {
            Version = 1,
            Settings = settings,
            Logic = logicFactory.Build(settings),
            CreatedAt = DateTimeOffset.UtcNow
        });

        logger.LogDebug("Settings generation 1 published with {AccountCount} account(s).",
            settings.Accounts.Count());
    }
}
