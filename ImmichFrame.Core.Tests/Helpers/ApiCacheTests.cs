using ImmichFrame.Core.Helpers;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Helpers;

[TestFixture]
public class ApiCacheTests
{
    [Test]
    public async Task ConcurrentMisses_RunTheFactoryOnce()
    {
        using var cache = new ApiCache(TimeSpan.FromHours(1));
        var calls = 0;
        var release = new TaskCompletionSource<int>();

        Task<int> Factory()
        {
            Interlocked.Increment(ref calls);
            return release.Task;
        }

        var pending = Enumerable.Range(0, 10).Select(_ => cache.GetOrAddAsync("key", Factory)).ToList();
        release.SetResult(42);
        var results = await Task.WhenAll(pending);

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(results, Is.All.EqualTo(42));
    }

    [Test]
    public async Task FailedLoad_IsEvictedAndRetried()
    {
        using var cache = new ApiCache(TimeSpan.FromHours(1));
        var calls = 0;

        Task<int> Factory() => ++calls == 1
            ? Task.FromException<int>(new HttpRequestException("down"))
            : Task.FromResult(7);

        Assert.ThrowsAsync<HttpRequestException>(() => cache.GetOrAddAsync("key", Factory));
        Assert.That(await cache.GetOrAddAsync("key", Factory), Is.EqualTo(7));
        Assert.That(await cache.GetOrAddAsync("key", Factory), Is.EqualTo(7));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void SynchronouslyThrowingFactory_IsEvictedAndRetried()
    {
        using var cache = new ApiCache(TimeSpan.FromHours(1));
        var calls = 0;

        Task<int> Factory()
        {
            calls++;
            throw new InvalidOperationException("boom");
        }

        Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync("key", Factory));
        Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync("key", Factory));
        Assert.That(calls, Is.EqualTo(2));
    }
}
