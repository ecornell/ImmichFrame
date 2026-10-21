using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace ImmichFrame.Core.Logic.Pool;

public class MemoryAssetsPool(ImmichApi immichApi, IAccountSettings accountSettings,
    IAssetShowHistory? showHistory = null) : CachingApiAssetsPool(new DailyApiCache(), immichApi, accountSettings, showHistory)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var searchDate = DateTimeOffset.Now;
        var memories = await immichApi.SearchMemoriesAsync(searchDate, null, null, null, null, null, ct);

        var memoryAssets = new List<AssetResponseDto>();
        foreach (var memory in memories)
        {
            var assets = (memory.Assets ?? []).ToList();
            // Data is only populated for "on this day" memories.
            int? yearsAgo = memory.Data != null ? searchDate.Year - memory.Data.Year : null;

            if (!accountSettings.ShowVideos)
            {
                assets = assets.Where(a => a.Type == AssetTypeEnum.IMAGE).ToList();
            }

            foreach (var asset in assets)
            {
                if (asset.ExifInfo == null)
                {
                    // One asset's details failing to load (deleted meanwhile, transient error) must not
                    // drop every memory; the asset is still shown, just without EXIF details.
                    try
                    {
                        var assetInfo = await immichApi.GetAssetInfoAsync(asset.Id, null, null, ct);
                        asset.ExifInfo = assetInfo?.ExifInfo;
                        asset.People = assetInfo?.People ?? asset.People;
                    }
                    catch (Exception) when (!ct.IsCancellationRequested)
                    {
                    }
                }

                if (yearsAgo is int years)
                {
                    // Immich can return an asset without EXIF even from the info endpoint.
                    asset.ExifInfo ??= new ExifResponseDto();
                    asset.ExifInfo.Description = $"{years} {(years == 1 ? "year" : "years")} ago";
                }
            }

            memoryAssets.AddRange(assets);
        }

        return memoryAssets;
    }
}

class DailyApiCache : ApiCache
{
    public DailyApiCache() : base(() => new MemoryCacheEntryOptions
    {
        AbsoluteExpiration = DateTimeOffset.Now.Date.AddDays(1)
    }
    )
    {
    }
}