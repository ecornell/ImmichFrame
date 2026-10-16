using ImmichFrame.Core.Api;
using ImmichFrame.Core.Logic.Pool;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Logic.Pool;

public class AssetShowHistoryTests
{
    private string _directory = null!;

    [SetUp]
    public void Setup()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"ImmichFrameShowHistory_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    private static AssetResponseDto[] Assets(int count) => Enumerable.Range(0, count)
        .Select(_ => new AssetResponseDto { Id = Guid.NewGuid() }).ToArray();

    private static Guid ShowNext(AssetShowHistory history, AssetResponseDto[] assets)
    {
        var id = history.PickLeastRecent(assets, 1).Single().Id;
        history.RecordShown(id);
        return id;
    }

    [Test]
    public void ShownAssets_WaitThreeQuartersOfThePoolBeforeRepeating()
    {
        var assets = Assets(237);
        var history = new AssetShowHistory();
        var lastShownAt = new Dictionary<Guid, int>();
        var minimumGap = int.MaxValue;
        for (var slide = 0; slide < 5_000; slide++)
        {
            var id = ShowNext(history, assets);
            if (lastShownAt.TryGetValue(id, out var previous)) minimumGap = Math.Min(minimumGap, slide - previous);
            lastShownAt[id] = slide;
        }

        // 60 of 237 are eligible, so an asset waits for the other 177 to pass it.
        Assert.That(minimumGap, Is.GreaterThanOrEqualTo(178));
        Assert.That(lastShownAt.Keys, Is.EquivalentTo(assets.Select(a => a.Id)));
    }

    [Test]
    public void NeverShownAssetIsPreferred()
    {
        var assets = Assets(4);
        var history = new AssetShowHistory();
        foreach (var asset in assets[..3]) history.RecordShown(asset.Id);

        Assert.That(history.PickLeastRecent(assets, 1).Single(), Is.SameAs(assets[3]));
    }

    [Test]
    public void PickingAloneDoesNotCountAsShown()
    {
        var assets = Assets(4);
        var history = new AssetShowHistory();
        history.PickLeastRecent(assets, 4);

        Assert.That(assets.Select(a => history.LastShown(a.Id)), Is.All.Null);
    }

    [Test]
    public void BatchesAreDistinctAndCappedAtTheCandidates()
    {
        var assets = Assets(32);
        var history = new AssetShowHistory();

        Assert.That(history.PickLeastRecent(assets.Concat(assets), 25).Select(a => a.Id), Is.Unique);
        Assert.That(history.PickLeastRecent(assets[..7], 25), Has.Count.EqualTo(7));
        Assert.That(history.PickLeastRecent([], 25), Is.Empty);
        Assert.That(history.PickLeastRecent(assets, 0), Is.Empty);
    }

    [Test]
    public void SameMillisecondRecordsKeepTheirOrder()
    {
        var assets = Assets(2);
        var history = new AssetShowHistory();
        history.RecordShown(assets[0].Id);
        history.RecordShown(assets[1].Id);

        Assert.That(history.LastShown(assets[1].Id), Is.GreaterThan(history.LastShown(assets[0].Id)));
    }

    [Test]
    public void SavedHistorySurvivesARestart()
    {
        var path = Path.Combine(_directory, "ShownAssets.json");
        var assets = Assets(4);
        var history = new AssetShowHistory();
        foreach (var asset in assets[..3]) history.RecordShown(asset.Id);

        Assert.That(history.Save(path), Is.True);
        Assert.That(history.Save(path), Is.False, "an unchanged history is not rewritten");

        var restarted = new AssetShowHistory();
        restarted.Load(path);
        Assert.That(assets.Select(a => restarted.LastShown(a.Id)), Is.EqualTo(assets.Select(a => history.LastShown(a.Id))));
        Assert.That(restarted.PickLeastRecent(assets, 1).Single(), Is.SameAs(assets[3]));
        Assert.That(Directory.GetFiles(_directory), Has.Length.EqualTo(1), "no temporary file is left behind");
    }

    [Test]
    public void LoadKeepsAssetsRecordedSinceStartup()
    {
        var path = Path.Combine(_directory, "ShownAssets.json");
        var asset = Assets(1).Single();
        var saved = new AssetShowHistory();
        saved.RecordShown(asset.Id);
        saved.Save(path);

        var current = new AssetShowHistory();
        current.RecordShown(asset.Id);
        var newer = current.LastShown(asset.Id);
        current.Load(path);

        Assert.That(current.LastShown(asset.Id), Is.EqualTo(Math.Max(newer!.Value, saved.LastShown(asset.Id)!.Value)));
        current.RecordShown(Guid.NewGuid());
        Assert.That(current.Save(path), Is.True);
    }

    [Test]
    public async Task ConcurrentPicksAndRecordsAreSafe()
    {
        var assets = Assets(100);
        var history = new AssetShowHistory();
        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => ShowNext(history, assets))));

        Assert.That(assets.Count(a => history.LastShown(a.Id) != null), Is.GreaterThan(0));
    }

    [Test]
    public async Task MultiAssetPoolBatchesAreDistinct()
    {
        var history = new AssetShowHistory();
        var assets = Assets(240);
        var album = new Mock<IAssetPool>();
        album.Setup(x => x.GetAssetCount(It.IsAny<CancellationToken>())).ReturnsAsync(assets.Length);
        album.Setup(x => x.GetAssets(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => history.PickLeastRecent(assets, 1));

        var batch = (await new MultiAssetPool([album.Object]).GetAssets(25)).ToList();

        Assert.That(batch, Has.Count.EqualTo(25));
        Assert.That(batch.Select(a => a.Id), Is.Unique);
    }
}
