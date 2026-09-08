using ImmichFrame.Core.Api;

namespace ImmichFrame.Core.Logic.Pool;

internal static class SearchAssetPagination
{
    public const int PageSize = 1000;

    public static int? NextPage(SearchAssetResponseDto page, int currentPage)
    {
        if (string.IsNullOrWhiteSpace(page.NextPage)) return null;

        if (!int.TryParse(page.NextPage, out var nextPage) || nextPage <= currentPage)
            throw new InvalidDataException($"Immich returned invalid nextPage token '{page.NextPage}'.");

        return nextPage;
    }
}
