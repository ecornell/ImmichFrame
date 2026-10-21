using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

/// <summary>
/// The whole library, sampled with Immich's random search.
/// </summary>
/// <remarks>
/// <c>ShowArchived</c> means "archived assets as well", as in the filter pools and the docs, not
/// "archived assets only". A random search takes a single visibility, so with it on the timeline
/// and the archive are searched separately and each search gets a share of the request in
/// proportion to how many assets it holds.
/// </remarks>
public class AllAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : IAssetPool
{
    public async Task<long> GetAssetCount(CancellationToken ct = default)
    {
        var (timeline, archive) = await VisibilityCounts(ct);
        return timeline + archive;
    }

    private async Task<(long Timeline, long Archive)> VisibilityCounts(CancellationToken ct)
    {
        // Retrieve total media count (images + videos); will update to query filtered stats from Immich
        var timeline = Count(await apiCache.GetOrAddAsync(nameof(AllAssetsPool),
            () => immichApi.GetAssetStatisticsAsync(null, false, AssetVisibility.Timeline, ct)));

        if (!accountSettings.ShowArchived) return (timeline, 0);

        var archive = Count(await apiCache.GetOrAddAsync($"{nameof(AllAssetsPool)}_Archive",
            () => immichApi.GetAssetStatisticsAsync(null, false, AssetVisibility.Archive, ct)));

        return (timeline, archive);
    }

    private long Count(AssetStatsResponseDto stats) =>
        accountSettings.ShowVideos ? stats.Images + stats.Videos : stats.Images;

    // Account filters (excluded albums, unsupported types, ...) run after the random search, so a
    // search can come back short. Top it up with a few larger searches; without this a single-asset
    // request fails about as often as the share of the library that is filtered out.
    private const int MaxSearchRounds = 3;
    private const int MinTopUpSize = 10;

    public async Task<IEnumerable<AssetResponseDto>> GetAssets(int requested, CancellationToken ct = default)
    {
        var excludedAssetIds = await apiCache.GetOrAddAsync(
            $"{nameof(AllAssetsPool)}_ExcludedAlbums",
            () => AssetHelper.GetExcludedAlbumAssetIds(immichApi, accountSettings, ct));

        var result = new List<AssetResponseDto>(Math.Max(requested, 0));
        var ids = new HashSet<Guid>();

        for (var round = 0; round < MaxSearchRounds && result.Count < requested; round++)
        {
            var missing = requested - result.Count;
            var size = round == 0 ? missing : Math.Max(missing * 2, MinTopUpSize);
            var (found, exhausted) = await Search(size, ct);

            foreach (var asset in found.ApplyAccountFilters(accountSettings, excludedAssetIds))
            {
                if (result.Count == requested) break;
                if (ids.Add(asset.Id)) result.Add(asset);
            }

            // A short search means the server has no more matching assets to offer.
            if (exhausted) break;
        }

        return result;
    }

    private async Task<(List<AssetResponseDto> Found, bool Exhausted)> Search(int size, CancellationToken ct)
    {
        if (!accountSettings.ShowArchived)
        {
            var found = (await immichApi.SearchRandomAsync(BuildSearch(size, AssetVisibility.Timeline), ct)).ToList();
            return (found, found.Count < size);
        }

        var (timelineCount, archiveCount) = await VisibilityCounts(ct);
        var archiveSize = ArchiveShare(size, timelineCount, archiveCount);
        var timelineSize = size - archiveSize;

        var timeline = timelineSize > 0
            ? immichApi.SearchRandomAsync(BuildSearch(timelineSize, AssetVisibility.Timeline), ct)
            : Task.FromResult<ICollection<AssetResponseDto>>([]);
        var archive = archiveSize > 0
            ? immichApi.SearchRandomAsync(BuildSearch(archiveSize, AssetVisibility.Archive), ct)
            : Task.FromResult<ICollection<AssetResponseDto>>([]);

        var timelineFound = (await timeline).ToList();
        var archiveFound = (await archive).ToList();

        var merged = timelineFound.Concat(archiveFound).ToArray();
        Random.Shared.Shuffle(merged);

        // Exhausted only when neither side can offer more: a side that got no share this round may
        // still hold matches unless it is empty altogether.
        var timelineDone = timelineSize > 0 ? timelineFound.Count < timelineSize : timelineCount <= 0;
        var archiveDone = archiveSize > 0 ? archiveFound.Count < archiveSize : archiveCount <= 0;
        return (merged.ToList(), timelineDone && archiveDone);
    }

    /// <summary>How many of <paramref name="size"/> picks go to the archive, sampled by asset count.</summary>
    private static int ArchiveShare(int size, long timelineCount, long archiveCount)
    {
        var total = timelineCount + archiveCount;
        if (archiveCount <= 0 || total <= 0) return 0;
        if (timelineCount <= 0) return size;

        var share = 0;
        for (var i = 0; i < size; i++)
        {
            if (Random.Shared.NextInt64(total) < archiveCount) share++;
        }

        return share;
    }

    private RandomSearchDto BuildSearch(int size, AssetVisibility visibility)
    {
        var searchDto = new RandomSearchDto
        {
            Size = size,
            WithExif = true,
            WithPeople = true,
            Visibility = visibility
        };

        if (!accountSettings.ShowVideos)
        {
            searchDto.Type = AssetTypeEnum.IMAGE;
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
