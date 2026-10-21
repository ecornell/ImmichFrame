using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImmichFrame.Core.Logic.AccountSelection;

/// <summary>
/// Picks accounts in proportion to their asset counts.
/// </summary>
/// <remarks>
/// An account whose Immich server fails (unreachable, rejected key, ...) is weighted 0 for
/// <see cref="FailureCooldown"/> rather than failing the request, so the remaining accounts keep the
/// slideshow going. After the cooldown it is tried again, which is how it recovers once its server
/// is back. Without the cooldown every pick would wait out the HTTP timeout on a dead server.
/// </remarks>
public class TotalAccountImagesSelectionStrategy : IAccountSelectionStrategy
{
    internal static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(60);

    private readonly ILogger<TotalAccountImagesSelectionStrategy> _logger;
    private readonly IAssetAccountTracker _tracker;
    private readonly IList<IAccountImmichFrameLogic> _accounts;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<IAccountImmichFrameLogic, DateTimeOffset> _failedUntil = new();

    public TotalAccountImagesSelectionStrategy(
        ILogger<TotalAccountImagesSelectionStrategy> logger,
        IAssetAccountTracker tracker,
        IList<IAccountImmichFrameLogic> accounts)
        : this(logger, tracker, accounts, TimeProvider.System)
    {
    }

    internal TotalAccountImagesSelectionStrategy(
        ILogger<TotalAccountImagesSelectionStrategy> logger,
        IAssetAccountTracker tracker,
        IList<IAccountImmichFrameLogic> accounts,
        TimeProvider time)
    {
        _logger = logger;
        _tracker = tracker;
        _accounts = accounts;
        _time = time;
    }

    public async Task<(IAccountImmichFrameLogic, AssetResponseDto)?> GetNextAsset()
    {
        ExceptionDispatchInfo? lastFailure = null;

        // A chosen account can still fail after its (cached) count succeeded; it is then cooled down
        // and the pick is retried among the rest.
        for (var attempt = 0; attempt < Math.Max(1, _accounts.Count); attempt++)
        {
            var chosen = await _accounts.ChooseOne(GetTotalForAccount);
            if (chosen == null)
            {
                // ChooseOne has nothing to pick when the weights sum to zero: no accounts, every
                // account reports an empty library, or every account is failing.
                lastFailure?.Throw();
                _logger.LogDebug("No account has any assets to choose from");
                return null;
            }

            AssetResponseDto? asset;
            try
            {
                asset = await chosen.GetNextAsset();
            }
            catch (Exception ex)
            {
                MarkFailed(chosen, ex);
                lastFailure = ExceptionDispatchInfo.Capture(ex);
                continue;
            }

            if (asset != null)
            {
                await _tracker.RecordAssetLocation(chosen, asset.Id);
                return (chosen, asset);
            }

            _logger.LogDebug("No next asset found");
            return null;
        }

        lastFailure?.Throw();
        return null;
    }

    private bool IsCoolingDown(IAccountImmichFrameLogic account) =>
        _failedUntil.TryGetValue(account, out var until) && _time.GetUtcNow() < until;

    private void MarkFailed(IAccountImmichFrameLogic account, Exception ex)
    {
        var alreadyFailing = _failedUntil.ContainsKey(account);
        _failedUntil[account] = _time.GetUtcNow() + FailureCooldown;

        // Warn once per outage, not on every retry.
        if (alreadyFailing)
            _logger.LogDebug("Account [{account}] is still failing: {message}", account, ex.Message);
        else
            _logger.LogWarning("Account [{account}] failed and is skipped for {cooldown}s: {message}",
                account, FailureCooldown.TotalSeconds, ex.Message);
    }

    private void MarkHealthy(IAccountImmichFrameLogic account)
    {
        if (_failedUntil.TryRemove(account, out _))
            _logger.LogInformation("Account [{account}] has recovered", account);
    }

    private async Task<(IList<long>, long)> GetWeights(IList<IAccountImmichFrameLogic> accounts)
    {
        var weights = await Task.WhenAll(accounts.Select(GetTotalForAccount));
        return (weights, weights.Sum());
    }

    private async Task<IList<double>> GetProportions(IList<IAccountImmichFrameLogic> accounts)
    {
        var (totals, sum) = await GetWeights(accounts);
        if (sum == 0)
        {
            // No account reported any assets — avoid 0/0 NaN proportions; uniform weights
            // keep the (empty) batches intact.
            return accounts.Select(_ => 1d).ToList();
        }

        return totals.Select(t => (double)t / sum).ToList();
    }

    private async Task<long> GetTotalForAccount(IAccountImmichFrameLogic account)
    {
        if (IsCoolingDown(account)) return 0;

        try
        {
            var total = await account.GetTotalAssets();
            MarkHealthy(account);
            return total;
        }
        catch (Exception ex)
        {
            MarkFailed(account, ex);
            return 0;
        }
    }

    private async Task<IEnumerable<AssetResponseDto>> GetAssetsForAccount(IAccountImmichFrameLogic account)
    {
        if (IsCoolingDown(account)) return [];

        try
        {
            return await account.GetAssets();
        }
        catch (Exception ex)
        {
            MarkFailed(account, ex);
            return [];
        }
    }

    public async Task<IEnumerable<(IAccountImmichFrameLogic, AssetResponseDto)>> GetAssets()
    {
        if (_accounts.Count == 0)
        {
            _logger.LogDebug("No accounts configured, returning no assets");
            return Enumerable.Empty<(IAccountImmichFrameLogic, AssetResponseDto)>();
        }

        var proportions = await GetProportions(_accounts);
        var maxAccount = proportions.Max();
        var adjustedProportions = proportions.Select(x => x / maxAccount).ToList();
        var assetLists = _accounts.Select(GetAssetsForAccount).ToList();

        var taskList = assetLists
            .Zip(_accounts, adjustedProportions)
            .Select(async tuple =>
            {
                var (task, account, proportion) = tuple;
                var assets = (await task).ToList();
                _logger.LogDebug("Retrieved {total} asset(s) for account [{account}], will take {proportion}%", assets.Count(), account, proportion * 100);
                return (account, assets.Shuffle().TakeProportional(proportion));
            });

        var accountAssetTupleList = await Task.WhenAll(taskList);

        _logger.LogDebug("Processing {} list(s) of asset(s) of length {}", accountAssetTupleList.Length, string.Join(",", accountAssetTupleList.Select(a => a.Item2.Count())));

        foreach (var accountAssetTuple in accountAssetTupleList)
        {
            foreach (var asset in accountAssetTuple.Item2)
            {
                await _tracker.RecordAssetLocation(accountAssetTuple.account, asset.Id);
            }
        }

        var assets = accountAssetTupleList.SelectMany(tuple => tuple.Item2.Select(asset => (tuple.account, asset))).ToList();

        _logger.LogDebug("Returning {count} asset(s)", assets.Count);

        return assets;
    }

    public async Task<T> ForAssetAsync<T>(Guid assetId, Func<IAccountImmichFrameLogic, Task<T>> f)
    {
        try
        {
            return await _tracker.ForAssetAsync(assetId, f);
        }
        catch (AssetNotFoundException)
        {
            // The tracker only knows assets served by this process and settings generation, so a
            // client still holding ids from before a restart or a settings save lands here. Ask
            // every account instead; with a single account this always finds it.
        }

        foreach (var account in _accounts)
        {
            T result;
            try
            {
                result = await f(account);
            }
            catch (ApiException e) when (e.StatusCode is 400 or 404)
            {
                // Same rule as the tracker: 400/404 means "not in this account", anything else is real.
                continue;
            }

            try
            {
                await _tracker.RecordAssetLocation(account, assetId);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not record the account for asset {assetId}", assetId);
            }

            return result;
        }

        _logger.LogError("Failed to locate account for asset {assetId}", assetId);
        throw new AssetNotFoundException();
    }
}