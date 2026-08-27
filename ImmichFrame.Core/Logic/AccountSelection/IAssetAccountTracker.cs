using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.AccountSelection;

public interface IAssetAccountTracker
{
    ValueTask<bool> RecordAssetLocation(IAccountImmichFrameLogic account, Guid assetId);
    T ForAsset<T>(Guid assetId, Func<IAccountImmichFrameLogic, T> f);

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