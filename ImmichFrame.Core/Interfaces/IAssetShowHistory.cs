using ImmichFrame.Core.Api;

namespace ImmichFrame.Core.Interfaces;

/// <summary>
/// When each asset was last shown on any client, so selection can favour the least recently shown.
/// </summary>
public interface IAssetShowHistory
{
    /// <summary>
    /// Picks up to <paramref name="requested"/> distinct assets at random from the least recently
    /// shown share of <paramref name="candidates"/>. Picking does not count as showing.
    /// </summary>
    IReadOnlyList<AssetResponseDto> PickLeastRecent(IEnumerable<AssetResponseDto> candidates, int requested);

    void RecordShown(Guid assetId);
}
