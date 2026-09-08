using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

public class PersonAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings) : CachingApiAssetsPool(apiCache, immichApi, accountSettings)
{
    protected override async Task<IEnumerable<AssetResponseDto>> LoadAssets(CancellationToken ct = default)
    {
        var personAssets = new List<AssetResponseDto>();

        var people = accountSettings.People;
        if (people == null)
        {
            return personAssets;
        }

        foreach (var personId in people)
        {
            int page = 1;
            while (true)
            {
                var metadataBody = new MetadataSearchDto
                {
                    Page = page,
                    Size = SearchAssetPagination.PageSize,
                    PersonIds = [personId],
                    WithExif = true,
                    WithPeople = true
                };

                if (!accountSettings.ShowVideos)
                {
                    metadataBody.Type = AssetTypeEnum.IMAGE;
                }

                var personInfo = await immichApi.SearchAssetsAsync(null, null, metadataBody, ct);

                personAssets.AddRange(personInfo.Assets.Items);
                var nextPage = SearchAssetPagination.NextPage(personInfo.Assets, page);
                if (!nextPage.HasValue) break;
                page = nextPage.Value;
            }
        }

        return personAssets;
    }
}