using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AlbumAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var albumAssets = new List<AssetResponseDto>();

        var albums = accountSettings.Albums;
        if (albums != null)
        {
            foreach (var albumId in albums)
            {
                int page = 1;
                while (true)
                {
                    var metadataBody = new MetadataSearchDto
                    {
                        Page = page,
                        Size = SearchAssetPagination.PageSize,
                        AlbumIds = [albumId],
                        WithExif = true,
                        WithPeople = true,
                    };
                    var searchResponse = await immichApi.SearchAssetsAsync(null, null, metadataBody, ct);

                    albumAssets.AddRange(searchResponse.Assets.Items);
                    var nextPage = SearchAssetPagination.NextPage(searchResponse.Assets, page);
                    if (!nextPage.HasValue) break;
                    page = nextPage.Value;
                }
            }
        }

        return albumAssets;
    }
}