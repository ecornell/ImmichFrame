using System.Globalization;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;
using ImmichFrame.WebApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace ImmichFrame.WebApi.Controllers
{
    public class ImageResponse
    {
        public required string RandomImageBase64 { get; set; }
        public required string ThumbHashImageBase64 { get; set; }
        public required string PhotoDate { get; set; }
        public required string ImageLocation { get; set; }
        public Guid? AssetId { get; set; }
        public AssetTypeEnum? AssetType { get; set; }
        public double? DurationSeconds { get; set; }
        // Only with includeMetadata=true, for clients that report what they show (e.g. to Home Assistant).
        public ImageMetadata? Metadata { get; set; }
    }

    /// <summary>Unformatted details of the chosen asset, unlike the display strings above.</summary>
    public class ImageMetadata
    {
        // The capture instant, not LocalDateTime, which is wall time labelled UTC. Immich reports the
        // capture time zone separately (an IANA name such as "America/New_York") when it knows it.
        public DateTimeOffset? TakenAt { get; set; }
        public string? TimeZone { get; set; }
        public string? Description { get; set; }
        public List<string> People { get; set; } = new();
        public List<string> Albums { get; set; } = new();
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Country { get; set; }
        public bool IsFavorite { get; set; }
        public string? ImmichServerUrl { get; set; }
    }


    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AssetController : ControllerBase
    {
        private readonly ILogger<AssetController> _logger;
        private readonly IImmichFrameLogic _logic;
        private readonly IGeneralSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly IAssetShowHistory _showHistory;

        // Long enough to cover a client fetching the bytes (or retrying) right after choosing an
        // asset, short enough not to swallow a genuine re-show of the same asset.
        private static readonly TimeSpan WebhookDedupeWindow = TimeSpan.FromMinutes(1);

        public AssetController(ILogger<AssetController> logger, IImmichFrameLogic logic, IGeneralSettings settings, IMemoryCache cache,
            IAssetShowHistory showHistory)
        {
            _logger = logger;
            _logic = logic;
            _settings = settings;
            _cache = cache;
            _showHistory = showHistory;
        }

        [HttpGet(Name = "GetAssets")]
        public async Task<List<AssetResponseDto>> GetAssets(string clientIdentifier = "")
        {
            var sanitizedClientIdentifier = clientIdentifier.SanitizeString();
            _logger.LogDebug("Assets requested by '{sanitizedClientIdentifier}'", sanitizedClientIdentifier);
            return (await _logic.GetAssets()).ToList();
        }

        [HttpGet("{id}/AssetInfo", Name = "GetAssetInfo")]
        public async Task<AssetResponseDto> GetAssetInfo(Guid id, string clientIdentifier = "")
        {
            var sanitizedClientIdentifier = clientIdentifier.SanitizeString();
            _logger.LogDebug("AssetInfo '{id}' requested by '{sanitizedClientIdentifier}'", id, sanitizedClientIdentifier);

            return await _logic.GetAssetInfoById(id);
        }

        [HttpGet("{id}/AssetFaces", Name = "GetAssetFaces")]
        public async Task<IEnumerable<AssetFaceResponseDto>> GetAssetFaces(Guid id, string clientIdentifier = "")
        {
            var sanitizedClientIdentifier = clientIdentifier.SanitizeString();
            _logger.LogDebug("AssetFaces '{id}' requested by '{sanitizedClientIdentifier}'", id, sanitizedClientIdentifier);

            return await _logic.GetAssetFacesById(id);
        }

        [HttpGet("{id}/AlbumInfo", Name = "GetAlbumInfo")]
        public async Task<List<AlbumResponseDto>> GetAlbumInfo(Guid id, string clientIdentifier = "")
        {
            var sanitizedClientIdentifier = clientIdentifier.SanitizeString();
            _logger.LogDebug("AlbumInfo '{id}' requested by '{sanitizedClientIdentifier}'", id, sanitizedClientIdentifier);

            return (await _logic.GetAlbumInfoById(id)).ToList() ?? throw new AssetNotFoundException("No asset was found");
        }

        [Obsolete("Use GetAsset instead.")]
        [HttpGet("{id}/Image", Name = "GetImage")]
        [Produces("image/jpeg", "image/webp")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetImage(Guid id, string clientIdentifier = "")
        {
            return await GetAsset(id, clientIdentifier, AssetTypeEnum.IMAGE);
        }

        [HttpGet("{id}/Asset", Name = "GetAsset")]
        [Produces("image/jpeg", "image/webp", "video/mp4", "video/quicktime")]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status206PartialContent)]
        [ProducesResponseType(StatusCodes.Status416RangeNotSatisfiable)]
        public async Task<IActionResult> GetAsset(Guid id, string clientIdentifier = "", AssetTypeEnum? assetType = null)
        {
            var sanitizedClientIdentifier = clientIdentifier.SanitizeString();
            _logger.LogDebug("Asset '{id}' requested by '{sanitizedClientIdentifier}' (type hint: {assetType})", id, sanitizedClientIdentifier, assetType);

            var rangeHeader = Request.Headers["Range"].FirstOrDefault();

            AssetResponse asset;
            try
            {
                asset = await _logic.GetAsset(id, assetType, rangeHeader);
            }
            catch (ApiException ex) when (ex.StatusCode == 416)
            {
                return StatusCode(StatusCodes.Status416RangeNotSatisfiable);
            }

            // Fetching the bytes is when the SPA shows an asset, video ranges included; re-recording
            // an asset already chosen by RandomImageAndInfo only refreshes its stamp.
            _showHistory.RecordShown(id);

            // One notification per asset shown. A plain image fetch is the "shown" event for the SPA;
            // it is skipped when RandomImageAndInfo already notified for this asset and client, and
            // for retries. Video streams never notify: their requests are ranged and repeated.
            if (string.IsNullOrEmpty(rangeHeader) && assetType != AssetTypeEnum.VIDEO &&
                asset.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                !_cache.TryGetValue(WebhookKey(id, sanitizedClientIdentifier), out _))
            {
                NotifyAssetShown(id, sanitizedClientIdentifier);
            }

            Response.Headers["Accept-Ranges"] = "bytes";

            if (asset.IsPartial && !string.IsNullOrEmpty(asset.ContentRange))
            {
                Response.Headers["Content-Range"] = asset.ContentRange;
                Response.StatusCode = 206;
                Response.ContentType = asset.ContentType;

                if (asset.FileStream is { CanSeek: true } && asset.FileStream.Length > 0)
                    Response.ContentLength = asset.FileStream.Length;
                else if (asset.ContentLength.HasValue)
                    Response.ContentLength = asset.ContentLength;

                using (asset.Owner)
                {
                    try
                    {
                        await asset.FileStream.CopyToAsync(Response.Body, HttpContext.RequestAborted);
                    }
                    catch (Exception ex) when (ex is OperationCanceledException or IOException &&
                                               HttpContext.RequestAborted.IsCancellationRequested)
                    {
                        // The player seeked or closed the stream; disposing the owner releases
                        // the upstream Immich response.
                        _logger.LogDebug("Client aborted video range request for '{id}'", id);
                    }
                }
                return new EmptyResult();
            }

            if (asset.Owner != null)
                HttpContext.Response.RegisterForDispose(asset.Owner);

            return File(asset.FileStream, asset.ContentType, asset.FileName, enableRangeProcessing: true);
        }

        [HttpGet("RandomImageAndInfo", Name = "GetRandomImageAndInfo")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(ImageResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ImageResponse>> GetRandomImageAndInfo(string clientIdentifier = "", bool includeVideos = false, bool includeImage = true,
            bool includeMetadata = false)
        {
            var sanitizedClientIdentifier = clientIdentifier.SanitizeString();
            _logger.LogDebug("Random image requested by '{sanitizedClientIdentifier}'", sanitizedClientIdentifier);

            var randomAsset = await PickRandomAsset(includeVideos);
            if (randomAsset == null)
            {
                _logger.LogWarning("No {kind} asset found for '{sanitizedClientIdentifier}'",
                    includeVideos ? "image or video" : "image", sanitizedClientIdentifier);
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "No asset found",
                    detail: includeVideos ? "No image or video asset was found" : "No image asset was found");
            }

            // Only the chosen asset counts as shown; rejected candidates stay least recently shown.
            _showHistory.RecordShown(randomAsset.Id);
            _logger.LogInformation("Showing {assetType} '{id}' on '{sanitizedClientIdentifier}'",
                randomAsset.Type, randomAsset.Id, sanitizedClientIdentifier);

            // Choosing the asset is the "shown" event for these clients, whether or not the image is
            // inlined. With includeImage=false they fetch the bytes from GetAsset, which then
            // does not notify again (see NotifyAssetShown).
            string randomImageBase64 = string.Empty;
            if (includeImage)
            {
                var asset = await _logic.GetAsset(randomAsset.Id, AssetTypeEnum.IMAGE);

                using (asset.Owner)
                await using (asset.FileStream)
                using (var memoryStream = new MemoryStream())
                {
                    await asset.FileStream.CopyToAsync(memoryStream);
                    randomImageBase64 = Convert.ToBase64String(memoryStream.ToArray());
                }
            }

            NotifyAssetShown(randomAsset.Id, sanitizedClientIdentifier);

            string thumbHashBase64 = string.Empty;
            using (var thumbhashImage = randomAsset.ThumbhashImage)
            {
                if (thumbhashImage != null)
                {
                    thumbhashImage.Position = 0;
                    using var thumbhashMemory = new MemoryStream();
                    await thumbhashImage.CopyToAsync(thumbhashMemory);
                    thumbHashBase64 = Convert.ToBase64String(thumbhashMemory.ToArray());
                }
            }

            CultureInfo cultureInfo = new CultureInfo(_settings.Language);
            string photoDateFormat = _settings.PhotoDateFormat!.Replace("''", "\\'");
            string photoDate = randomAsset.LocalDateTime.ToString(photoDateFormat, cultureInfo) ?? string.Empty;

            var locationFormat = _settings.ImageLocationFormat ?? "City,State,Country";
            var imageLocation = locationFormat
                .Replace("City", randomAsset.ExifInfo?.City ?? string.Empty)
                .Replace("State", randomAsset.ExifInfo?.State ?? string.Empty)
                .Replace("Country", randomAsset.ExifInfo?.Country ?? string.Empty);
            imageLocation = string.Join(",", imageLocation.Split(',').Where(s => !string.IsNullOrWhiteSpace(s)));

            return new ImageResponse
            {
                RandomImageBase64 = randomImageBase64,
                ThumbHashImageBase64 = thumbHashBase64,
                PhotoDate = photoDate,
                ImageLocation = imageLocation,
                AssetId = randomAsset.Id,
                AssetType = randomAsset.Type,
                DurationSeconds = randomAsset.Type == AssetTypeEnum.VIDEO && randomAsset.Duration > 0
                    ? randomAsset.Duration / 1000.0
                    : null,
                Metadata = includeMetadata ? await BuildMetadata(randomAsset) : null
            };
        }

        private async Task<ImageMetadata> BuildMetadata(AssetResponseDto asset)
        {
            var exif = asset.ExifInfo;
            return new ImageMetadata
            {
                TakenAt = exif?.DateTimeOriginal ?? asset.FileCreatedAt,
                TimeZone = NullIfBlank(exif?.TimeZone),
                Description = NullIfBlank(exif?.Description),
                // The pools load people with the asset; unnamed and hidden faces are not people to report.
                People = (asset.People ?? Enumerable.Empty<PersonResponseDto>())
                    .Where(p => !p.IsHidden && !string.IsNullOrWhiteSpace(p.Name))
                    .Select(p => p.Name)
                    .ToList(),
                Albums = await GetAlbumNames(asset.Id),
                City = NullIfBlank(exif?.City),
                State = NullIfBlank(exif?.State),
                Country = NullIfBlank(exif?.Country),
                IsFavorite = asset.IsFavorite,
                ImmichServerUrl = asset.ImmichServerUrl
            };
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        // Album membership costs an Immich call per asset, so it is cached for the same window the
        // pools cache album contents. A failed lookup omits the albums rather than the image.
        private async Task<List<string>> GetAlbumNames(Guid assetId)
        {
            var key = ("AssetAlbums", assetId);
            if (_cache.TryGetValue(key, out List<string>? cached) && cached != null)
                return cached;

            try
            {
                var names = (await _logic.GetAlbumInfoById(assetId))
                    .Select(a => a.AlbumName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList();
                _cache.Set(key, names, TimeSpan.FromHours(Math.Max(1, _settings.RefreshAlbumPeopleInterval)));
                return names;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Unable to load albums for asset '{id}'", assetId);
                return new List<string>();
            }
        }

        private static (string, Guid, string) WebhookKey(Guid id, string clientIdentifier)
            => ("AssetWebhook", id, clientIdentifier);

        private void NotifyAssetShown(Guid id, string sanitizedClientIdentifier)
        {
            _cache.Set(WebhookKey(id, sanitizedClientIdentifier), true, WebhookDedupeWindow);
            var notification = new AssetRequestedNotification(id, sanitizedClientIdentifier);
            _ = _logic.SendWebhookNotification(notification);
        }

        // Batches beyond the first asset, used when the next asset is of a type the client can't show.
        private const int MaxFallbackBatches = 3;

        private async Task<AssetResponseDto?> PickRandomAsset(bool includeVideos)
        {
            // Older native clients cannot play videos, so they stay opt-in. The returned
            // image of a video is its Immich preview, used as a poster frame.
            bool IsAcceptable(AssetResponseDto asset) =>
                asset.Type == AssetTypeEnum.IMAGE || (includeVideos && asset.Type == AssetTypeEnum.VIDEO);

            // A rejected candidate is recorded as if shown, so it ages out of the least recently shown
            // share instead of filling it for a client that can never show it.
            bool Accept(AssetResponseDto asset)
            {
                if (IsAcceptable(asset)) return true;
                _showHistory.RecordShown(asset.Id);
                return false;
            }

            // The single next asset is the common case.
            var next = await _logic.GetNextAsset();
            if (next == null) return null;
            if (Accept(next)) return next;

            // Otherwise sample whole batches, so a video-heavy library (or AUDIO/OTHER assets)
            // doesn't need one round trip per rejected asset.
            for (int i = 0; i < MaxFallbackBatches; i++)
            {
                var batch = (await _logic.GetAssets()).ToList();
                if (batch.Count == 0) return null;
                var match = batch.FirstOrDefault(Accept);
                if (match != null) return match;
            }

            return null;
        }
    }
}
