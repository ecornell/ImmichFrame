using ImmichFrame.Core.Api;

namespace ImmichFrame.Core.Logic.Pool;

public interface IAssetPool
{
    Task<long> GetAssetCount(CancellationToken ct = default);
    Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default);

    protected static async Task<IEnumerable<AssetResponseDto>> WaitAssets(
        int requested,
        Func<CancellationToken, Task<AssetResponseDto?>> supplier,
        CancellationToken? cancellationToken = null)
    {
        //allow up to one minute
        var ct = cancellationToken ?? new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;

        var itemsRead = new List<AssetResponseDto>(requested > 0 ? requested : 0);
        var ids = new HashSet<Guid>();

        // Each supplied asset is picked independently, so skip repeats, with a bounded number of
        // extra attempts for pools smaller than the request.
        for (var attempt = 0; itemsRead.Count < requested && attempt < requested * 3; attempt++)
        {
            var asset = await supplier(ct);

            if (asset == null) break;
            if (ids.Add(asset.Id)) itemsRead.Add(asset);
        }

        return itemsRead;
    }
}