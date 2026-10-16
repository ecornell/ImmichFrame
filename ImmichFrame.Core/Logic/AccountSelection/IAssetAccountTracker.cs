using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.AccountSelection;

public interface IAssetAccountTracker
{
    ValueTask<bool> RecordAssetLocation(IAccountImmichFrameLogic account, Guid assetId);
    /// <summary>
    /// Runs <paramref name="f"/> against each account whose filter may contain the asset, falling
    /// through to the next candidate when an account reports the asset as not found (a Bloom filter
    /// false positive).
    /// </summary>
    Task<T> ForAssetAsync<T>(Guid assetId, Func<IAccountImmichFrameLogic, Task<T>> f);

    /// <summary>
    /// Drops the tracking state for accounts that have been replaced by a settings reload.
    /// </summary>
    /// <remarks>
    /// Call this on a delay, not at the moment of the swap: replacement accounts start with empty
    /// filters, so the asset currently on screen can only still be resolved through the retired
    /// entries. Evicting immediately would make the in-flight image 404.
    /// </remarks>
    void Forget(IEnumerable<IAccountImmichFrameLogic> accounts);
}