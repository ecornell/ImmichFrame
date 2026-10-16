using ImmichFrame.Core.Api;
using ImmichFrame.Core.Logic.Pool;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Helpers;

[TestFixture]
public class AssetShowHistoryPersistenceTests
{
    private string _directory = null!;

    [SetUp]
    public void Setup() =>
        _directory = Path.Combine(Path.GetTempPath(), $"ImmichFrameShowHistory_{Guid.NewGuid():N}");

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private AssetShowHistoryPersistence Persistence(AssetShowHistory history) =>
        new(history, new ConfigLocation(_directory), NullLogger<AssetShowHistoryPersistence>.Instance);

    [Test]
    public async Task HistoryIsSavedAtShutdownAndLoadedAtStartup()
    {
        var shown = new AssetResponseDto { Id = Guid.NewGuid() };
        var unseen = new AssetResponseDto { Id = Guid.NewGuid() };
        var history = new AssetShowHistory();
        var first = Persistence(history);
        await first.StartAsync(CancellationToken.None);
        history.RecordShown(shown.Id);
        await first.StopAsync(CancellationToken.None);

        var restarted = new AssetShowHistory();
        var second = Persistence(restarted);
        await second.StartAsync(CancellationToken.None);
        await second.StopAsync(CancellationToken.None);

        Assert.That(restarted.PickLeastRecent([shown, unseen], 1).Single(), Is.SameAs(unseen));
    }

    [Test]
    public async Task DamagedHistoryIsIgnored()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, AssetShowHistoryPersistence.FileName), "{not json");
        var history = new AssetShowHistory();
        var persistence = Persistence(history);

        await persistence.StartAsync(CancellationToken.None);
        history.RecordShown(Guid.NewGuid());
        await persistence.StopAsync(CancellationToken.None);

        var restarted = new AssetShowHistory();
        restarted.Load(Path.Combine(_directory, AssetShowHistoryPersistence.FileName));
    }
}
