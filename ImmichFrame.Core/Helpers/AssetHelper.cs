// ImmichFrame.Core/Helpers/AssetHelper.cs
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;

namespace ImmichFrame.Core.Helpers;

public static class AssetHelper
{
    /// <summary>
    /// The ids of every asset in the account's excluded albums. Only ids are kept: each pool caches
    /// this for its refresh window, and full DTOs (with EXIF and people) for a large excluded album
    /// would be held in memory several times over for nothing.
    /// </summary>
    /// <remarks>Typed as <see cref="IEnumerable{T}"/> for the cache; the value is a <see cref="HashSet{T}"/>.</remarks>
    public static async Task<IEnumerable<Guid>> GetExcludedAlbumAssetIds(ImmichApi immichApi, IAccountSettings accountSettings, CancellationToken ct = default)
    {
        var excludedAlbumAssets = new HashSet<Guid>();

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

                excludedAlbumAssets.UnionWith(searchResponse.Assets.Items.Select(asset => asset.Id));
                var nextPage = SearchAssetPagination.NextPage(searchResponse.Assets, page);
                if (!nextPage.HasValue) break;
                page = nextPage.Value;
            }
        }

        return excludedAlbumAssets;
    }
}