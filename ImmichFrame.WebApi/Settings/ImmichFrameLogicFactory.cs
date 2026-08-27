using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic;
using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Builds a complete account graph from an explicit <see cref="ServerSettings"/> instance.
/// </summary>
public sealed class ImmichFrameLogicFactory(
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    Func<IList<IAccountImmichFrameLogic>, IAccountSelectionStrategy> strategyFactory)
{
    /// <summary>
    /// Constructs the graph for <paramref name="settings"/> without publishing it, so a candidate can
    /// be proven to build before anything is written or swapped.
    /// </summary>
    public IImmichFrameLogic Build(ServerSettings settings)
    {
        // Hand the candidate's general settings to each account explicitly. Letting ActivatorUtilities
        // resolve IGeneralSettings from DI would return the *currently published* generation, which
        // during a reload is still the old one — silently giving new accounts a stale ApiCache TTL
        // (RefreshAlbumPeopleInterval is read in PooledImmichFrameLogic's constructor).
        var general = settings.GeneralSettings;

        return new MultiImmichFrameLogicDelegate(
            settings,
            account => ActivatorUtilities.CreateInstance<PooledImmichFrameLogic>(services, account, general),
            loggerFactory.CreateLogger<MultiImmichFrameLogicDelegate>(),
            strategyFactory);
    }
}
