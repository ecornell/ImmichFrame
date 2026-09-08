using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class FavoriteAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var favoriteAssets = new List<AssetResponseDto>();

        int page = 1;
        while (true)
        {
            var metadataBody = new MetadataSearchDto
            {
                Page = page,
                Size = SearchAssetPagination.PageSize,
                IsFavorite = true,
                WithExif = true,
                WithPeople = true
            };

            if (!accountSettings.ShowVideos)
            {
                metadataBody.Type = AssetTypeEnum.IMAGE;
            }

            var favoriteInfo = await immichApi.SearchAssetsAsync(null, null, metadataBody, ct);

            favoriteAssets.AddRange(favoriteInfo.Assets.Items);
            var nextPage = SearchAssetPagination.NextPage(favoriteInfo.Assets, page);
            if (!nextPage.HasValue) break;
            page = nextPage.Value;
        }

        return favoriteAssets;
    }
}