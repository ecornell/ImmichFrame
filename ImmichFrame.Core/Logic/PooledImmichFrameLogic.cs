using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;
using ImmichFrame.Core.Models;

namespace ImmichFrame.Core.Logic;

public class PooledImmichFrameLogic : IAccountImmichFrameLogic
{
    private readonly IGeneralSettings _generalSettings;
    private readonly IApiCache _apiCache;
    private readonly IAssetPool _pool;
    private readonly ImmichApi _immichApi;
    private readonly string _downloadLocation;
    private readonly object _imageCacheLocksGate = new();
    private readonly Dictionary<Guid, ImageCacheLock> _imageCacheLocks = [];

    public PooledImmichFrameLogic(IAccountSettings accountSettings, IGeneralSettings generalSettings, IHttpClientFactory httpClientFactory)
        : this(accountSettings, generalSettings, httpClientFactory,
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageCache"))
    {
    }

    internal PooledImmichFrameLogic(IAccountSettings accountSettings, IGeneralSettings generalSettings,
        IHttpClientFactory httpClientFactory, string downloadLocation)
    {
        _generalSettings = generalSettings;
        _downloadLocation = downloadLocation;

        var httpClient = httpClientFactory.CreateClient("ImmichApiAccountClient");
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
            pools.Add(new FavoriteAssetsPool(_apiCache, _immichApi, accountSettings));

        if (accountSettings.ShowMemories)
            pools.Add(new MemoryAssetsPool(_immichApi, accountSettings));

        if (hasAlbums)
            pools.Add(new AlbumAssetsPool(_apiCache, _immichApi, accountSettings));

        if (hasPeople)
            pools.Add(new PersonAssetsPool(_apiCache, _immichApi, accountSettings));

        if (hasTags)
            pools.Add(new TagAssetsPool(_apiCache, _immichApi, accountSettings));

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
        if (!_generalSettings.DownloadImages)
        {
            var uncachedResponse = await DownloadImage(id);
            var (uncachedFileName, uncachedContentType) = GetImageMetadata(id, uncachedResponse);
            return CreateImageResponse(uncachedFileName, uncachedContentType,
                uncachedResponse.Stream, uncachedResponse);
        }

        Directory.CreateDirectory(_downloadLocation);
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
