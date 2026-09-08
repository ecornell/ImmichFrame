// ImmichFrame.Core/Helpers/AssetHelper.cs
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;

namespace ImmichFrame.Core.Helpers;

public static class AssetHelper
{
    public static async Task<IEnumerable<AssetResponseDto>> GetExcludedAlbumAssets(ImmichApi immichApi, IAccountSettings accountSettings, CancellationToken ct = default)
    {
        var excludedAlbumAssets = new List<AssetResponseDto>();

        foreach (var albumId in accountSettings?.ExcludedAlbums ?? new())
        {
            int page = 1;
            while (true)
            {
                var metadataBody = new MetadataSearchDto
                {
                    Page = page,
                    Size = SearchAssetPagination.PageSize,
                    AlbumIds = [albumId]
                };
                var searchResponse = await immichApi.SearchAssetsAsync(null, null, metadataBody, ct);

                if (searchResponse.Assets == null) break;

                excludedAlbumAssets.AddRange(searchResponse.Assets.Items);
                var nextPage = SearchAssetPagination.NextPage(searchResponse.Assets, page);
                if (!nextPage.HasValue) break;
                page = nextPage.Value;
            }
        }

        return excludedAlbumAssets;
    }
}