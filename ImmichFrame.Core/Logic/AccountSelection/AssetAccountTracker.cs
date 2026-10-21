using System.Collections.Concurrent;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.AccountSelection;

/// <summary>
/// Remembers exactly which account served each asset.
/// </summary>
/// <remarks>
/// This replaced a per-account Bloom filter. The filters were sized once from the account's asset
/// count at first use and never resized, so a growing library (or a pool whose count was 0 at that
/// moment, which made the filter builder throw) degraded into constant false positives. An exact
/// map has no false positives, no sizing, and no Immich call at record time. Its size is bounded by
/// the number of distinct assets served per settings generation (retired accounts are removed by
/// <see cref="Forget"/>); at roughly 50 bytes an entry, the same order as the exact
/// <c>AssetShowHistory</c> map the app already keeps.
/// </remarks>
public class AssetAccountTracker : IAssetAccountTracker
{
    private readonly ConcurrentDictionary<Guid, IAccountImmichFrameLogic> _assetToAccount = new();

    public ValueTask<bool> RecordAssetLocation(IAccountImmichFrameLogic account, Guid assetId)
    {
        var changed = true;
        _assetToAccount.AddOrUpdate(assetId, account, (_, existing) =>
        {
            changed = !ReferenceEquals(existing, account);
            return account;
        });
        return ValueTask.FromResult(changed);
    }

    public IAccountImmichFrameLogic? GetAccount(Guid assetId) =>
        _assetToAccount.TryGetValue(assetId, out var account) ? account : null;

    public void Forget(IEnumerable<IAccountImmichFrameLogic> accounts)
    {
        var retired = accounts.ToHashSet();
        if (retired.Count == 0) return;

        foreach (var entry in _assetToAccount)
        {
            if (retired.Contains(entry.Value))
            {
                // Only if still mapped to the retired account; a newer generation may have re-recorded it.
                _assetToAccount.TryRemove(entry);
            }
        }
    }
}
