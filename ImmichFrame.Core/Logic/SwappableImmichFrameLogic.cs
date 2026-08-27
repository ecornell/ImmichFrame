using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;

namespace ImmichFrame.Core.Logic;

/// <summary>
/// A stable <see cref="IImmichFrameLogic"/> singleton that forwards to whichever delegate is current.
/// </summary>
/// <remarks>
/// <see cref="MultiImmichFrameLogicDelegate"/> freezes its account map at construction, and each
/// <see cref="PooledImmichFrameLogic"/> bakes the API key, server URL, cache TTL and pool topology
/// into its own constructor — so applying an account change means building a replacement graph.
/// This facade lets that replacement happen underneath long-lived consumers.
/// <para>
/// Each call resolves the delegate exactly once, so a swap that lands mid-request cannot tear it:
/// the request simply finishes against the graph it started on.
/// </para>
/// </remarks>
public sealed class SwappableImmichFrameLogic(Func<IImmichFrameLogic> current) : IImmichFrameLogic
{
    public Task<AssetResponseDto?> GetNextAsset() => current().GetNextAsset();

    public Task<IEnumerable<AssetResponseDto>> GetAssets() => current().GetAssets();

    public Task<AssetResponseDto> GetAssetInfoById(Guid assetId) => current().GetAssetInfoById(assetId);

    public Task<IEnumerable<AssetFaceResponseDto>> GetAssetFacesById(Guid assetId) =>
        current().GetAssetFacesById(assetId);

    public Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId) =>
        current().GetAlbumInfoById(assetId);

    public Task<AssetResponse> GetAsset(Guid id, AssetTypeEnum? assetType = null, string? rangeHeader = null) =>
        current().GetAsset(id, assetType, rangeHeader);

    public Task<long> GetTotalAssets() => current().GetTotalAssets();

    public Task SendWebhookNotification(IWebhookNotification notification) =>
        current().SendWebhookNotification(notification);
}
