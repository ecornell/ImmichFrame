using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.AccountSelection;

public interface IAssetAccountTracker
{
    /// <summary>Records that <paramref name="account"/> served <paramref name="assetId"/>.</summary>
    /// <returns><c>true</c> if this changed the recorded account.</returns>
    ValueTask<bool> RecordAssetLocation(IAccountImmichFrameLogic account, Guid assetId);

    /// <summary>The account that last served <paramref name="assetId"/>, or <c>null</c> if none did.</summary>
    /// <remarks>
    /// May be an account of a replaced settings generation that has not been forgotten yet, which is
    /// what lets the image on screen during a save still be fetched.
    /// </remarks>
    IAccountImmichFrameLogic? GetAccount(Guid assetId);

    /// <summary>
    /// Drops the tracking state for accounts that have been replaced by a settings reload.
    /// </summary>
    /// <remarks>
    /// Call this on a delay, not at the moment of the swap: replacement accounts have recorded
    /// nothing yet, so the asset currently on screen can only still be resolved through the retired
    /// entries. Evicting immediately would make the in-flight image 404.
    /// </remarks>
    void Forget(IEnumerable<IAccountImmichFrameLogic> accounts);
}
