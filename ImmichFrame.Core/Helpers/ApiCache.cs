using Microsoft.Extensions.Caching.Memory;

namespace ImmichFrame.Core.Helpers;

public class ApiCache : IApiCache, IDisposable
{
    private readonly Func<MemoryCacheEntryOptions> _cacheOptions;
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly object _gate = new();

    public ApiCache(TimeSpan cacheDuration) : this(() => new MemoryCacheEntryOptions()
    {
        AbsoluteExpirationRelativeToNow = cacheDuration
    })
    {
    }

    public ApiCache(Func<MemoryCacheEntryOptions> entryOptions)
    {
        _cacheOptions = entryOptions;
    }

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, running <paramref name="factory"/> at most
    /// once per entry even under concurrent callers.
    /// </summary>
    /// <remarks>
    /// <see cref="CacheExtensions.GetOrCreateAsync{TItem}(IMemoryCache, object, Func{ICacheEntry, Task{TItem}})"/>
    /// does not de-duplicate: concurrent misses each run the factory, so a cold start (or every
    /// refresh) fetched a whole album or library once per in-flight request. The entry therefore
    /// holds a <see cref="Lazy{T}"/> of the load, created under a lock, and a failed load is evicted
    /// so the next call retries instead of replaying the fault until expiry.
    /// </remarks>
    public virtual async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory)
    {
        Lazy<Task<T>> lazy;
        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out lazy!))
            {
                lazy = new Lazy<Task<T>>(factory);
                _cache.Set(key, lazy, _cacheOptions());
            }
        }

        try
        {
            return await lazy.Value;
        }
        catch
        {
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out Lazy<Task<T>>? current) && ReferenceEquals(current, lazy))
                    _cache.Remove(key);
            }

            throw;
        }
    }

    public void Dispose()
        => _cache.Dispose();
}
