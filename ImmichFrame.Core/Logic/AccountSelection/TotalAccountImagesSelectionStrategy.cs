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

    /// <summary>
    /// How long an account's count or batch may take before it is treated as failing, when other
    /// accounts could serve instead. The Immich client has no timeout of its own below HttpClient's
    /// 100 s default (it also streams videos), so a black-holed server would otherwise stall every
    /// pick for that long. The abandoned call keeps running; a later success is cached as usual.
    /// </summary>
    internal static readonly TimeSpan DefaultAccountCallTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<TotalAccountImagesSelectionStrategy> _logger;
    private readonly IAssetAccountTracker _tracker;
    private readonly IList<IAccountImmichFrameLogic> _accounts;
    private readonly TimeProvider _time;
    private readonly TimeSpan _accountCallTimeout;
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
        TimeProvider time,
        TimeSpan? accountCallTimeout = null)
    {
        _logger = logger;
        _tracker = tracker;
        _accounts = accounts;
        _time = time;
        _accountCallTimeout = accountCallTimeout ?? DefaultAccountCallTimeout;
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

    // With a single account there is nothing else to serve, so waiting is better than giving up
    // (a first load of a large album can legitimately take a while).
    private Task<T> Bounded<T>(Task<T> call) =>
        _accounts.Count > 1 ? call.WaitAsync(_accountCallTimeout) : call;

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
            var total = await Bounded(account.GetTotalAssets());
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
            return await Bounded(account.GetAssets());
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
        // The account that served the asset first. The tracker only knows assets served by this
        // process (a retired generation's accounts included, during their grace period), so a client
        // still holding ids from before a restart falls through to asking every account; with a
        // single account this always finds it.
        var tracked = _tracker.GetAccount(assetId);
        var candidates = tracked is null
            ? _accounts
            : _accounts.Where(account => !ReferenceEquals(account, tracked)).Prepend(tracked);

        ExceptionDispatchInfo? unreachable = null;

        foreach (var account in candidates)
        {
            T result;
            try
            {
                result = await f(account);
            }
            catch (ApiException e) when (e.StatusCode is 400 or 404)
            {
                // Immich answers 400/404 for an asset that is not in this account.
                continue;
            }
            catch (Exception e) when (IsAccountUnavailable(e))
            {
                // This account could not answer at all (down, timed out, key rejected). Another
                // account on the same server may still have the asset. Remembered so that, if no one
                // has it, the caller sees the outage (502) rather than a misleading 404.
                _logger.LogDebug("Account [{account}] could not be asked for asset {assetId}: {message}", account, assetId, e.Message);
                unreachable = ExceptionDispatchInfo.Capture(e);
                continue;
            }
            // Anything else (e.g. 416 for a bad Range, or a 500) is a real answer and propagates.

            if (!ReferenceEquals(account, tracked))
            {
                try
                {
                    await _tracker.RecordAssetLocation(account, assetId);
                }
                catch (Exception e)
                {
                    _logger.LogWarning(e, "Could not record the account for asset {assetId}", assetId);
                }
            }

            return result;
        }

        unreachable?.Throw();

        _logger.LogError("Failed to locate account for asset {assetId}", assetId);
        throw new AssetNotFoundException();
    }

    private static bool IsAccountUnavailable(Exception e) => e switch
    {
        ApiException { StatusCode: 401 or 403 } => true,
        HttpRequestException => true,
        // HttpClient's own timeout, as opposed to a caller cancelling.
        TaskCanceledException { InnerException: TimeoutException } => true,
        _ => false
    };
}
