using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;
using ImmichFrame.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImmichFrame.Core.Logic;

public class PooledImmichFrameLogic : IAccountImmichFrameLogic
{
    private readonly IGeneralSettings _generalSettings;
    private readonly IApiCache _apiCache;
    private readonly IAssetPool _pool;
    private readonly IAssetShowHistory _showHistory;
    private readonly ImmichApi _immichApi;
    private readonly string _downloadLocation;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly object _imageCacheLocksGate = new();
    private readonly Dictionary<Guid, ImageCacheLock> _imageCacheLocks = [];
    private readonly Lazy<bool> _cacheWritable;
    private long _nextSweepTicks;

    /// <summary>How often expired files are swept from the image cache.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <summary>Temporary download files older than this are leftovers from a crash.</summary>
    private static readonly TimeSpan StaleTemporaryFileAge = TimeSpan.FromHours(1);

    public PooledImmichFrameLogic(IAccountSettings accountSettings, IGeneralSettings generalSettings,
        IHttpClientFactory httpClientFactory, IAssetShowHistory showHistory,
        ImageCacheLocation? imageCacheLocation = null, ILogger<PooledImmichFrameLogic>? logger = null)
        : this(accountSettings, generalSettings, httpClientFactory,
            (imageCacheLocation ?? ImageCacheLocation.Default).Directory, showHistory, logger)
    {
    }

    internal PooledImmichFrameLogic(IAccountSettings accountSettings, IGeneralSettings generalSettings,
        IHttpClientFactory httpClientFactory, string downloadLocation, IAssetShowHistory? showHistory = null,
        ILogger? logger = null, TimeProvider? timeProvider = null)
    {
        _generalSettings = generalSettings;
        _showHistory = showHistory ?? new AssetShowHistory();
        _downloadLocation = downloadLocation;
        _logger = logger ?? NullLogger.Instance;
        _time = timeProvider ?? TimeProvider.System;
        _cacheWritable = new Lazy<bool>(ProbeCacheWritable);

        var httpClient = httpClientFactory.CreateClient(ImmichApiHttpClientExtensions.ImmichApiAccountClient);
        AccountSettings = accountSettings;

        httpClient.UseApiKey(accountSettings.ApiKey);
        _immichApi = new ImmichApi(accountSettings.ImmichServerUrl, httpClient);

        _apiCache = new ApiCache(RefreshInterval(generalSettings.RefreshAlbumPeopleInterval));
        _pool = BuildPool(accountSettings);
    }

    private static TimeSpan RefreshInterval(int hours)
        => hours > 0 ? TimeSpan.FromHours(hours) : TimeSpan.FromMilliseconds(1);

    public IAccountSettings AccountSettings { get; }

    private IAssetPool BuildPool(IAccountSettings accountSettings)
    {
        var hasAlbums = accountSettings.Albums?.Any() ?? false;
        var hasPeople = accountSettings.People?.Any() ?? false;
        var hasTags = accountSettings.Tags?.Any() ?? false;

        if (!accountSettings.ShowFavorites && !accountSettings.ShowMemories && !hasAlbums && !hasPeople && !hasTags)
        {
            return new AllAssetsPool(_apiCache, _immichApi, accountSettings);
        }

        var pools = new List<IAssetPool>();

        if (accountSettings.ShowFavorites)
            pools.Add(new FavoriteAssetsPool(_apiCache, _immichApi, accountSettings, _showHistory));

        if (accountSettings.ShowMemories)
            pools.Add(new MemoryAssetsPool(_immichApi, accountSettings, _showHistory));

        if (hasAlbums)
            pools.Add(new AlbumAssetsPool(_apiCache, _immichApi, accountSettings, _showHistory));

        if (hasPeople)
            pools.Add(new PersonAssetsPool(_apiCache, _immichApi, accountSettings, _showHistory));

        if (hasTags)
            pools.Add(new TagAssetsPool(_apiCache, _immichApi, accountSettings, _showHistory));

        return new MultiAssetPool(pools);
    }

    public async Task<AssetResponseDto?> GetNextAsset()
    {
        return (await _pool.GetAssets(1)).FirstOrDefault();
    }

    public async Task<IEnumerable<AssetResponseDto>> GetAssets()
    {
        return await _pool.GetAssets(25);
    }

    public async Task<AssetResponseDto> GetAssetInfoById(Guid assetId) => await _immichApi.GetAssetInfoAsync(assetId, null, null);

    public async Task<IEnumerable<AssetFaceResponseDto>> GetAssetFacesById(Guid assetId) => await _immichApi.GetFacesAsync(assetId);

    public async Task<IEnumerable<AlbumResponseDto>> GetAlbumInfoById(Guid assetId) => await _immichApi.GetAllAlbumsAsync(assetId, null, null, null, null);

    public async Task<long> GetTotalAssets() => await _pool.GetAssetCount();

    public async Task<AssetResponse> GetAsset(Guid id, AssetTypeEnum? assetType = null, string? rangeHeader = null)
    {
        if (!assetType.HasValue)
        {
            var assetInfo = await _immichApi.GetAssetInfoAsync(id, null, null);
            if (assetInfo == null)
                throw new AssetNotFoundException($"Assetinfo for asset '{id}' was not found!");
            assetType = assetInfo.Type;
        }

        if (assetType == AssetTypeEnum.IMAGE)
        {
            return await GetImageAsset(id);
        }

        if (assetType == AssetTypeEnum.VIDEO)
        {
            return await GetVideoAsset(id, rangeHeader);
        }

        throw new AssetNotFoundException($"Asset {id} is not a supported media type ({assetType}).");
    }
    private async Task<AssetResponse> GetImageAsset(Guid id)
    {
        if (!_generalSettings.DownloadImages || !_cacheWritable.Value)
        {
            var uncachedResponse = await DownloadImage(id);
            var (uncachedFileName, uncachedContentType) = GetImageMetadata(id, uncachedResponse);
            return CreateImageResponse(uncachedFileName, uncachedContentType,
                uncachedResponse.Stream, uncachedResponse);
        }

        SweepIfDue();
        using var cacheLock = await AcquireImageCacheLock(id);
        var cached = FindFreshCachedImage(id);
        if (cached != null)
        {
            return CreateImageResponse(Path.GetFileName(cached), GetCachedContentType(cached),
                File.OpenRead(cached));
        }

        using var response = await DownloadImage(id);
        var (fileName, contentType) = GetImageMetadata(id, response);
        var filePath = Path.Combine(_downloadLocation, fileName);
        var temporaryPath = Path.Combine(_downloadLocation, $".{fileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var destination = new FileStream(temporaryPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await response.Stream.CopyToAsync(destination);
                await destination.FlushAsync();
                destination.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return CreateImageResponse(fileName, contentType, File.OpenRead(filePath));
    }

    /// <summary>
    /// Checked once per account graph (rebuilt on every settings save). The container runs as a
    /// non-root user, so a cache directory under a root-owned path is not writable; images are then
    /// streamed uncached instead of failing every request.
    /// </summary>
    private bool ProbeCacheWritable()
    {
        try
        {
            Directory.CreateDirectory(_downloadLocation);
            var probe = Path.Combine(_downloadLocation, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Image cache directory {Directory} is not writable ({Message}); images will be streamed without caching.",
                _downloadLocation, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts a background sweep at most once per <see cref="SweepInterval"/>. Reading the cache only
    /// prunes the requested asset, so without this, files of assets never requested again pile up.
    /// </summary>
    private void SweepIfDue()
    {
        var now = _time.GetUtcNow().UtcTicks;
        var due = Interlocked.Read(ref _nextSweepTicks);
        if (now < due) return;
        if (Interlocked.CompareExchange(ref _nextSweepTicks, now + SweepInterval.Ticks, due) != due) return;

        _ = Task.Run(SweepImageCache);
    }

    internal void SweepImageCache()
    {
        try
        {
            var now = _time.GetUtcNow().UtcDateTime;
            foreach (var file in Directory.EnumerateFiles(_downloadLocation))
            {
                try
                {
                    var age = now - File.GetCreationTimeUtc(file);
                    var isTemporary = Path.GetFileName(file).StartsWith('.');
                    var expired = isTemporary
                        ? age > StaleTemporaryFileAge
                        : _generalSettings.RenewImagesDuration <= age.Days;

                    if (expired) File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogDebug("Could not remove cached image {File}: {Message}", file, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Image cache sweep of {Directory} failed: {Message}", _downloadLocation, ex.Message);
        }
    }

    internal int ImageCacheLockCount
    {
        get
        {
            lock (_imageCacheLocksGate) return _imageCacheLocks.Count;
        }
    }

    private async Task<IDisposable> AcquireImageCacheLock(Guid id)
    {
        ImageCacheLock cacheLock;
        lock (_imageCacheLocksGate)
        {
            if (!_imageCacheLocks.TryGetValue(id, out cacheLock!))
            {
                cacheLock = new ImageCacheLock();
                _imageCacheLocks.Add(id, cacheLock);
            }
            cacheLock.ReferenceCount++;
        }

        try
        {
            await cacheLock.Semaphore.WaitAsync();
            return new ImageCacheLockLease(this, id, cacheLock);
        }
        catch
        {
            ReleaseImageCacheLockReference(id, cacheLock, releaseSemaphore: false);
            throw;
        }
    }

    private void ReleaseImageCacheLockReference(Guid id, ImageCacheLock cacheLock,
        bool releaseSemaphore)
    {
        if (releaseSemaphore) cacheLock.Semaphore.Release();

        lock (_imageCacheLocksGate)
        {
            cacheLock.ReferenceCount--;
            if (cacheLock.ReferenceCount != 0) return;

            _imageCacheLocks.Remove(id);
            cacheLock.Semaphore.Dispose();
        }
    }

    private sealed class ImageCacheLock
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class ImageCacheLockLease(
        PooledImmichFrameLogic owner, Guid id, ImageCacheLock cacheLock) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.ReleaseImageCacheLockReference(id, cacheLock, releaseSemaphore: true);
        }
    }

    private async Task<FileResponse> DownloadImage(Guid id)
    {
        var response = await _immichApi.ViewAssetAsync(null, id, string.Empty, AssetMediaSize.Preview, null);
        return response ?? throw new AssetNotFoundException($"Asset {id} was not found!");
    }

    private string? FindFreshCachedImage(Guid id)
    {
        foreach (var file in Directory.EnumerateFiles(_downloadLocation, $"{id}.*"))
        {
            if (_generalSettings.RenewImagesDuration > (DateTime.UtcNow - File.GetCreationTimeUtc(file)).Days)
                return file;

            File.Delete(file);
        }

        return null;
    }

    private static (string fileName, string contentType) GetImageMetadata(Guid id, FileResponse response)
    {
        var contentType = response.Headers.TryGetValue("Content-Type", out var values)
            ? values.FirstOrDefault() ?? "image/jpeg"
            : "image/jpeg";
        var extension = contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ? "webp" : "jpeg";
        return ($"{id}.{extension}", contentType);
    }

    private static string GetCachedContentType(string file) =>
        Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase)
            ? "image/webp"
            : "image/jpeg";

    private static AssetResponse CreateImageResponse(string fileName, string contentType, Stream stream,
        IDisposable? owner = null) => new()
    {
        FileName = fileName,
        ContentType = contentType,
        FileStream = stream,
        ContentRange = null,
        IsPartial = false,
        Owner = owner,
        ContentLength = null
    };

    private async Task<AssetResponse> GetVideoAsset(Guid id, string? rangeHeader = null)
    {
        var videoResponse = string.IsNullOrEmpty(rangeHeader)
            ? await _immichApi.PlayAssetVideoAsync(id, null, null)
            : await _immichApi.PlayAssetVideoWithRangeAsync(id, rangeHeader);

        var contentType = videoResponse.Headers.TryGetValue("Content-Type", out var ct)
            ? ct.FirstOrDefault() ?? "video/mp4"
            : "video/mp4";

        var contentRange = videoResponse.Headers.TryGetValue("Content-Range", out var cr)
            ? cr.FirstOrDefault()
            : null;

        long? contentLength = videoResponse.Headers.TryGetValue("Content-Length", out var cl)
            && long.TryParse(cl.FirstOrDefault(), out var clValue) ? clValue : null;

        return new AssetResponse
        {
            FileName = $"{id}.mp4",
            ContentType = contentType,
            FileStream = videoResponse.Stream,
            ContentRange = contentRange,
            IsPartial = videoResponse.StatusCode == 206,
            Owner = videoResponse,
            ContentLength = contentLength
        };
    }
    public async Task SendWebhookNotification(IWebhookNotification notification) =>
        await WebhookHelper.SendWebhookNotification(notification, _generalSettings.Webhook);

    public override string ToString() => $"Account Pool [{_immichApi.BaseUrl}]";
}
