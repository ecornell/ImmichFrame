using ImmichFrame.Core.Api;
using ImmichFrame.Core.Logic.Pool;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Logic.Pool;

public class AssetShuffleCycleTests
{
    private static AssetResponseDto[] Assets(int count) => Enumerable.Range(0, count)
        .Select(_ => new AssetResponseDto { Id = Guid.NewGuid() }).ToArray();

    [Test]
    public void SeptemberAlbum_EachCycleContainsAll332AssetsExactlyOnce()
    {
        var assets = Assets(332);
        var cycle = new AssetShuffleCycle();
        Guid? last = null;
        for (var round = 0; round < 5; round++)
        {
            var selected = Enumerable.Range(0, 332)
                .Select(_ => cycle.Take(assets, 1).Single().Id).ToArray();
            Assert.That(selected, Is.EquivalentTo(assets.Select(asset => asset.Id)));
            Assert.That(selected[0], Is.Not.EqualTo(last));
            last = selected[^1];
        }
    }

    [Test]
    public void Batches_StopAtCycleBoundary_AndDeduplicateOverlappingSources()
    {
        var assets = Assets(32);
        var cycle = new AssetShuffleCycle();
        var first = cycle.Take(assets.Concat(assets), 25);
        var last = cycle.Take(assets, 25);
        Assert.That(last, Has.Length.EqualTo(7));
        Assert.That(first.Concat(last).Select(a => a.Id), Is.Unique);
        Assert.That(cycle.Take(assets, 25), Has.Length.EqualTo(25));
    }

    [Test]
    public void Refresh_PreservesProgress_AndRemovesIneligibleAssets()
    {
        var assets = Assets(10);
        var cycle = new AssetShuffleCycle();
        var seen = cycle.Take(assets, 4).Select(a => a.Id).ToHashSet();
        var removed = assets.First(a => !seen.Contains(a.Id));
        var added = Assets(1).Single();
        var refreshed = assets.Where(a => a.Id != removed.Id)
            .Select(a => new AssetResponseDto { Id = a.Id }).Append(added).ToArray();
        var remaining = cycle.Take(refreshed, 20);
        Assert.That(remaining, Has.Length.EqualTo(6));
        Assert.That(remaining.Any(a => seen.Contains(a.Id) || a.Id == removed.Id), Is.False);
        Assert.That(remaining.Select(a => a.Id), Does.Contain(added.Id));
    }

    [Test]
    public void EmptySingleAndZeroRequests_AreSafe()
    {
        var cycle = new AssetShuffleCycle();
        var assets = Assets(1);
        Assert.That(cycle.Take([], 25), Is.Empty);
        Assert.That(cycle.Take(assets, 0), Is.Empty);
        Assert.That(cycle.Take(assets, -1), Is.Empty);
        Assert.That(cycle.Take(assets, 25).Single(), Is.SameAs(assets[0]));
        Assert.That(cycle.Take(assets, 25).Single(), Is.SameAs(assets[0]));
    }

    [Test]
    public async Task ConcurrentRequests_DoNotRepeatWithinCycle()
    {
        var cycle = new AssetShuffleCycle();
        var assets = Assets(332);
        var selected = await Task.WhenAll(Enumerable.Range(0, 332)
            .Select(_ => Task.Run(() => cycle.Take(assets, 1).Single().Id)));
        Assert.That(selected, Is.Unique);
    }
}
