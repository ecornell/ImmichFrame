using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class AllAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        // Retrieve total media count (images + videos); will update to query filtered stats from Immich
        var stats = await apiCache.GetOrAddAsync(nameof(AllAssetsPool),
            () => immichApi.GetAssetStatisticsAsync(null, false, null, ct));

        if (accountSettings.ShowVideos)
        {
            return stats.Images + stats.Videos;
        }

        return stats.Images;
    }

    // Account filters (excluded albums, unsupported types, ...) run after the random search, so a
    // search can come back short. Top it up with a few larger searches; without this a single-asset
    // request fails about as often as the share of the library that is filtered out.
    private const int MaxSearchRounds = 3;
    private const int MinTopUpSize = 10;

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        var excludedAlbumAssets = await apiCache.GetOrAddAsync(
            $"{nameof(AllAssetsPool)}_ExcludedAlbums",
            () => AssetHelper.GetExcludedAlbumAssets(immichApi, accountSettings, ct));

        var result = new List<AssetResponseDto>(Math.Max(requested, 0));
        var ids = new HashSet<Guid>();

        for (var round = 0; round < MaxSearchRounds && result.Count < requested; round++)
        {
            var missing = requested - result.Count;
            var size = round == 0 ? missing : Math.Max(missing * 2, MinTopUpSize);
            var found = (await immichApi.SearchRandomAsync(BuildSearch(size), ct)).ToList();

            foreach (var asset in found.ApplyAccountFilters(accountSettings, excludedAlbumAssets))
            {
                if (result.Count == requested) break;
                if (ids.Add(asset.Id)) result.Add(asset);
            }

            // A short search means the server has no more matching assets to offer.
            if (found.Count < size) break;
        }

        return result;
    }

    private RandomSearchDto BuildSearch(int size)
    {
        var searchDto = new RandomSearchDto
        {
            Size = size,
            WithExif = true,
            WithPeople = true
        };

        if (!accountSettings.ShowVideos)
        {
            searchDto.Type = AssetTypeEnum.IMAGE;
        }

        if (accountSettings.ShowArchived)
        {
            searchDto.Visibility = AssetVisibility.Archive;
        }
        else
        {
            searchDto.Visibility = AssetVisibility.Timeline;
        }

        var takenBefore = accountSettings.ImagesUntilDate.HasValue ? accountSettings.ImagesUntilDate : null;
        if (takenBefore.HasValue)
        {
            searchDto.TakenBefore = takenBefore;
        }
        var takenAfter = accountSettings.ImagesFromDate.HasValue ? accountSettings.ImagesFromDate : accountSettings.ImagesFromDays.HasValue ? DateTime.Today.AddDays(-accountSettings.ImagesFromDays.Value) : null;

        if (takenAfter.HasValue)
        {
            searchDto.TakenAfter = takenAfter;
        }

        if (accountSettings.Rating is int rating)
        {
            searchDto.Rating = rating;
        }

        return searchDto;
    }

}