using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public abstract class CachingApiAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings,
    IAssetShowHistory? showHistory = null) : IAssetPool
{
    private readonly IAssetShowHistory _showHistory = showHistory ?? new AssetShowHistory();

    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        // MultiAssetPool asks every sub-pool for its count on every pick, so this must not walk the
        // list. The cached value is already filtered and de-duplicated (see AllAssets).
        var assets = await AllAssets(ct);
        return assets.TryGetNonEnumeratedCount(out var count)
            ? count
            : assets.DistinctBy(asset => asset.Id).LongCount();
    }

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        if (requested <= 0) return [];
        var assets = await AllAssets(ct);
        ct.ThrowIfCancellationRequested();
        return _showHistory.PickLeastRecent(assets, requested);
    }

    private async Task<IEnumerable<AssetResponseDto>> AllAssets(CancellationToken ct = default)
    {
        var excludedAssetIds = await apiCache.GetOrAddAsync($"{GetType().FullName}_ExcludedAlbums", () => AssetHelper.GetExcludedAlbumAssetIds(immichApi, accountSettings, ct));

        // Materialized once per cache window. ApplyAccountFilters returns a deferred query, so caching
        // it as-is re-ran every filter (and the de-duplication) over the whole library on each count
        // and each pick.
        return await apiCache.GetOrAddAsync<IEnumerable<AssetResponseDto>>(GetType().FullName!, async () =>
            (await LoadAssets(ct).ApplyAccountFilters(accountSettings, excludedAssetIds))
                .DistinctBy(asset => asset.Id)
                .ToArray());
    }

    protected abstract Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default);
}
