using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public abstract class CachingApiAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    private readonly AssetShuffleCycle _cycle = new();

    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        return (await AllAssets(ct)).DistinctBy(asset => asset.Id).LongCount();
    }

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        if (requested <= 0) return [];
        var assets = await AllAssets(ct);
        ct.ThrowIfCancellationRequested();
        return _cycle.Take(assets, requested);
    }

    private async Task<IEnumerable<AssetResponseDto>> AllAssets(CancellationToken ct = default)
    {
        var excludedAlbumAssets = await apiCache.GetOrAddAsync($"{GetType().FullName}_ExcludedAlbums", () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings, ct));

        return await apiCache.GetOrAddAsync(GetType().FullName!, () => LoadAssets(ct).ApplyAccountFilters(accountSettings, excludedAlbumAssets));
    }

    protected abstract Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default);
}
