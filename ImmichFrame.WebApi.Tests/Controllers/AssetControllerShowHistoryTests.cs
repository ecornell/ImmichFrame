using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;
using ImmichFrame.WebApi.Controllers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers;

[TestFixture]
public class AssetControllerShowHistoryTests
{
    private static AssetResponseDto Asset(AssetTypeEnum type) =>
        new() { Id = Guid.NewGuid(), Type = type, LocalDateTime = DateTimeOffset.UtcNow };

    private static AssetController Controller(Mock<IImmichFrameLogic> logic, IAssetShowHistory history)
    {
        var settings = new Mock<IGeneralSettings>();
        settings.SetupGet(x => x.Language).Returns("en");
        settings.SetupGet(x => x.PhotoDateFormat).Returns("yyyy-MM-dd");
        return new AssetController(NullLogger<AssetController>.Instance, logic.Object, settings.Object,
            new MemoryCache(new MemoryCacheOptions()), history);
    }

    [Test]
    public async Task RandomImageAndInfo_RecordsTheChosenAndRejectedAssets()
    {
        var video = Asset(AssetTypeEnum.VIDEO);
        var image = Asset(AssetTypeEnum.IMAGE);
        var logic = new Mock<IImmichFrameLogic>();
        logic.Setup(x => x.GetNextAsset()).ReturnsAsync(video);
        logic.Setup(x => x.GetAssets()).ReturnsAsync([video, image]);
        var history = new Mock<IAssetShowHistory>();

        var response = await Controller(logic, history.Object)
            .GetRandomImageAndInfo("frame", includeVideos: false, includeImage: false);

        Assert.That(response.Value!.AssetId, Is.EqualTo(image.Id));
        history.Verify(x => x.RecordShown(image.Id), Times.Once);
        history.Verify(x => x.RecordShown(video.Id), Times.AtLeastOnce);
    }

    [Test]
    public async Task RandomImageAndInfo_WithNoAcceptableAssetAgesOutTheRejected()
    {
        var video = Asset(AssetTypeEnum.VIDEO);
        var logic = new Mock<IImmichFrameLogic>();
        logic.Setup(x => x.GetNextAsset()).ReturnsAsync(video);
        logic.Setup(x => x.GetAssets()).ReturnsAsync([video]);
        var history = new Mock<IAssetShowHistory>();

        await Controller(logic, history.Object).GetRandomImageAndInfo("frame", includeVideos: false, includeImage: false);

        history.Verify(x => x.RecordShown(video.Id), Times.AtLeastOnce);
    }

    [Test]
    public async Task ImageOnlyClientIsNotStarvedByUnshownVideos()
    {
        // A third of the pool is videos that no client shows, and the images were all shown recently,
        // so at first the least recently shown share holds only videos.
        var history = new AssetShowHistory();
        var images = Enumerable.Range(0, 200).Select(_ => Asset(AssetTypeEnum.IMAGE)).ToArray();
        var videos = Enumerable.Range(0, 100).Select(_ => Asset(AssetTypeEnum.VIDEO)).ToArray();
        foreach (var image in images) history.RecordShown(image.Id);
        var pool = images.Concat(videos).ToArray();
        var logic = new Mock<IImmichFrameLogic>();
        logic.Setup(x => x.GetNextAsset()).ReturnsAsync(() => history.PickLeastRecent(pool, 1).Single());
        // Production batches are 25 independent picks (AggregatingAssetPool).
        logic.Setup(x => x.GetAssets()).ReturnsAsync(() =>
            Enumerable.Range(0, 25).Select(_ => history.PickLeastRecent(pool, 1).Single()).ToList());
        var controller = Controller(logic, history);

        var found = 0;
        for (var request = 0; request < 10; request++)
        {
            var response = await controller.GetRandomImageAndInfo("frame", includeVideos: false, includeImage: false);
            if (response.Value != null) found++;
        }

        Assert.That(found, Is.GreaterThanOrEqualTo(7));
    }
}
