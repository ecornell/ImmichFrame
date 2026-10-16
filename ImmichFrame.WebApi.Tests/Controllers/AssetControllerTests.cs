using System;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using ImmichFrame.Core.Api;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Tests.Mocks;
using ImmichFrame.Core.Interfaces; // Added this back
using ImmichFrame.Core.Models;
using System.Text.Json;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers
{
    [TestFixture]
    public class AssetControllerTests
    {
        private WebApplicationFactory<Program> _factory;
        private Mock<HttpMessageHandler> _mockHttpMessageHandler;
        // Read when the test server is built, so tests may change it before CreateClient().
        private bool _showVideos;
        // When set before CreateClient(), replaces the real logic so tests can script assets and
        // observe webhook notifications (WebhookHelper posts with its own HttpClient).
        private Mock<IImmichFrameLogic>? _mockLogic;

        [SetUp]
        public void Setup()
        {
            _showVideos = false;
            _mockLogic = null;
            _mockHttpMessageHandler = new Mock<HttpMessageHandler>().WithServerVersion();

            _factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.UseMockHandler(_mockHttpMessageHandler);

                        // Directly instantiate and register settings objects
                        var generalSettings = new GeneralSettings
                        {
                            ShowWeatherDescription = false, // Assuming this corresponds to ShowWeather
                            WeatherIconUrl = "https://openweathermap.org/img/wn/{IconId}.png",
                            ShowClock = true,
                            ClockFormat = "HH:mm",
                            ClockDateFormat = "eee, MMM d",
                            Language = "en",
                            PhotoDateFormat = "MM/dd/yyyy", // Crucial for the NRE
                            ImageLocationFormat = "City,State,Country",
                            DownloadImages = false,
                            RenewImagesDuration = 30,
                            // Ensure all non-nullable string properties that might be used have defaults if not set here
                            PrimaryColor = "#FFFFFF", // Example default
                            SecondaryColor = "#000000", // Example default
                            Style = "none",
                            BaseFontSize = "16px",
                            WeatherApiKey = "",
                            UnitSystem = "imperial",
                            WeatherLatLong = "0,0"
                        };

                        var accountSettings = new ServerAccountSettings
                        {
                            ImmichServerUrl = "http://mock-immich-server.com",
                            ApiKey = "test-api-key",
                            ShowMemories = false,
                            ShowFavorites = true,
                            ShowArchived = false,
                            ShowVideos = _showVideos,
                            Albums = new List<Guid>(),
                            ExcludedAlbums = new List<Guid>(),
                            People = new List<Guid>()
                        };

                        var serverSettings = new ServerSettings
                        {
                            GeneralSettingsImpl = generalSettings,
                            AccountsImpl = new List<ServerAccountSettings> { accountSettings }
                        };

                        services.AddSingleton<IServerSettings>(serverSettings);
                        services.AddSingleton<IGeneralSettings>(generalSettings);
                        if (_mockLogic != null)
                            services.AddSingleton(_mockLogic.Object);
                        // Ensure IAccountSettings can be resolved if needed by MultiImmichFrameLogicDelegate directly
                        // However, PooledImmichFrameLogic receives IAccountSettings via the factory Func
                    });
                });
        }

        // Removed OneTimeSetup that created Settings.json

        [TearDown]
        public void TearDown()
        {
            _factory.Dispose();
        }

        [Test]
        public async Task GetRandomImage_ReturnsImageFromMockServer()
        {
            // Arrange
            var expectedAssetId = Guid.NewGuid();

            // Build the mock response from the generated DTOs so the test fails at
            // compile time (not at runtime) if the OpenAPI schema changes.
            var searchResponse = new SearchResponseDto
            {
                Assets = new SearchAssetResponseDto
                {
                    Count = 1,
                    Total = 1,
                    Items = { BuildAssetResponse(expectedAssetId) },
                    NextPage = null,
                },
                // Albums is already initialised to an empty SearchAlbumResponseDto.
            };

            var jsonResponse = JsonSerializer.Serialize(searchResponse);

            // Setup for SearchAssetsAsync
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/search/metadata")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() => new HttpResponseMessage // Use a Func to return new instance each time
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse)
                });

            // Setup for ViewAssetAsync (thumbnail)
            var mockImageData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // Minimal JPEG
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/thumbnail")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() => new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(mockImageData)
                });

            var client = _factory.CreateClient();

            // Act
            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo");

            // Assert
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            // For now, we'll just check if the response is not empty.
            // A more robust check would be to deserialize the response and check the asset ID.
            Assert.That(content, Is.Not.Empty);
        }

        [Test]
        public async Task GetRandomImageAndInfo_IncludeVideos_ReturnsVideoPosterAndDuration()
        {
            _showVideos = true;
            var videoId = Guid.NewGuid();
            SetupSearchAndThumbnail(BuildAssetResponse(videoId, AssetTypeEnum.VIDEO, duration: 12_500));
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo?includeVideos=true");

            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.That(root.GetProperty("assetId").GetGuid(), Is.EqualTo(videoId));
            Assert.That(root.GetProperty("assetType").GetInt32(), Is.EqualTo((int)AssetTypeEnum.VIDEO));
            Assert.That(root.GetProperty("durationSeconds").GetDouble(), Is.EqualTo(12.5));
            Assert.That(root.GetProperty("randomImageBase64").GetString(), Is.Not.Empty);
        }

        [Test]
        public async Task GetRandomImageAndInfo_WithoutIncludeVideos_OnlyVideos_Returns404()
        {
            _showVideos = true;
            SetupSearchAndThumbnail(BuildAssetResponse(Guid.NewGuid(), AssetTypeEnum.VIDEO, duration: 12_500));
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.That(json.RootElement.GetProperty("status").GetInt32(), Is.EqualTo(404));
            Assert.That(json.RootElement.GetProperty("detail").GetString(), Is.EqualTo("No image asset was found"));
        }

        [Test]
        public async Task GetRandomImageAndInfo_WithoutIncludeVideos_SkipsVideoAndAudioInBatch()
        {
            var video = BuildAssetResponse(Guid.NewGuid(), AssetTypeEnum.VIDEO, duration: 12_500);
            var audio = BuildAssetResponse(Guid.NewGuid(), AssetTypeEnum.AUDIO);
            var image = BuildAssetResponse(Guid.NewGuid());
            _mockLogic = CreateMockLogic();
            _mockLogic.Setup(l => l.GetNextAsset()).ReturnsAsync(video);
            _mockLogic.Setup(l => l.GetAssets()).ReturnsAsync(new[] { video, audio, image });
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo");

            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.That(json.RootElement.GetProperty("assetId").GetGuid(), Is.EqualTo(image.Id));
            Assert.That(json.RootElement.GetProperty("assetType").GetInt32(), Is.EqualTo((int)AssetTypeEnum.IMAGE));
            _mockLogic.Verify(l => l.GetNextAsset(), Times.Once());
            _mockLogic.Verify(l => l.GetAssets(), Times.Once());
        }

        [Test]
        public async Task GetRandomImageAndInfo_IncludeVideos_ReturnsImageWithoutDuration()
        {
            _showVideos = true;
            var imageId = Guid.NewGuid();
            SetupSearchAndThumbnail(BuildAssetResponse(imageId));
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo?includeVideos=true");

            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.That(root.GetProperty("assetId").GetGuid(), Is.EqualTo(imageId));
            Assert.That(root.GetProperty("assetType").GetInt32(), Is.EqualTo((int)AssetTypeEnum.IMAGE));
            Assert.That(root.GetProperty("durationSeconds").ValueKind, Is.EqualTo(JsonValueKind.Null));
        }

        [Test]
        public async Task GetRandomImageAndInfo_WithoutImage_ReturnsMetadataAndServesRawBytesSeparately()
        {
            _showVideos = true;
            var videoId = Guid.NewGuid();
            SetupSearchAndThumbnail(BuildAssetResponse(videoId, AssetTypeEnum.VIDEO, duration: 12_500));
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo?includeVideos=true&includeImage=false");

            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            Assert.That(root.GetProperty("randomImageBase64").GetString(), Is.Empty);
            Assert.That(root.GetProperty("assetId").GetGuid(), Is.EqualTo(videoId));
            Assert.That(root.GetProperty("durationSeconds").GetDouble(), Is.EqualTo(12.5));
            _mockHttpMessageHandler.Protected().Verify(
                "SendAsync",
                Times.Never(),
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/thumbnail")),
                ItExpr.IsAny<CancellationToken>());

            // The client then fetches the same poster/preview as raw bytes.
            var bytes = await client.GetByteArrayAsync($"/api/Asset/{videoId}/Asset?assetType=IMAGE");
            Assert.That(bytes, Is.EqualTo(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }));
        }

        [Test]
        public async Task Webhook_RandomImageAndInfoWithoutImage_NotifiesOnceIncludingByteFetch()
        {
            var image = BuildAssetResponse(Guid.NewGuid());
            _mockLogic = CreateMockLogic();
            _mockLogic.Setup(l => l.GetNextAsset()).ReturnsAsync(image);
            var client = _factory.CreateClient();

            var response = await client.GetAsync("/api/Asset/RandomImageAndInfo?includeImage=false&clientIdentifier=frame1");
            response.EnsureSuccessStatusCode();
            VerifyWebhooks(image.Id, Times.Once());

            // The follow-up byte fetch (and a retry of it) is part of the same showing.
            await client.GetByteArrayAsync($"/api/Asset/{image.Id}/Asset?assetType=IMAGE&clientIdentifier=frame1");
            await client.GetByteArrayAsync($"/api/Asset/{image.Id}/Asset?assetType=IMAGE&clientIdentifier=frame1");
            VerifyWebhooks(image.Id, Times.Once());
        }

        [Test]
        public async Task Webhook_ImageFetch_NotifiesOncePerShowing()
        {
            // The SPA's path: GetAssets, then one GetAsset per image shown.
            var id = Guid.NewGuid();
            _mockLogic = CreateMockLogic();
            var client = _factory.CreateClient();

            await client.GetByteArrayAsync($"/api/Asset/{id}/Asset?assetType=IMAGE&clientIdentifier=spa");
            await client.GetByteArrayAsync($"/api/Asset/{id}/Asset?assetType=IMAGE&clientIdentifier=spa");

            VerifyWebhooks(id, Times.Once());
        }

        [Test]
        public async Task Webhook_VideoRequest_DoesNotNotify()
        {
            var id = Guid.NewGuid();
            _mockLogic = CreateMockLogic();
            var client = _factory.CreateClient();

            var bytes = await client.GetByteArrayAsync($"/api/Asset/{id}/Asset?assetType=VIDEO&clientIdentifier=spa");

            Assert.That(bytes, Is.Not.Empty);
            VerifyWebhooks(id, Times.Never());
        }

        private void VerifyWebhooks(Guid assetId, Times times)
            => _mockLogic!.Verify(l => l.SendWebhookNotification(
                It.Is<IWebhookNotification>(n => n is AssetRequestedNotification
                    && ((AssetRequestedNotification)n).RequestedAssetId == assetId)), times);

        private static Mock<IImmichFrameLogic> CreateMockLogic()
        {
            var mock = new Mock<IImmichFrameLogic>();
            mock.Setup(l => l.GetAsset(It.IsAny<Guid>(), It.IsAny<AssetTypeEnum?>(), It.IsAny<string?>()))
                .ReturnsAsync((Guid id, AssetTypeEnum? type, string? _) => new AssetResponse
                {
                    FileName = $"{id}.{(type == AssetTypeEnum.VIDEO ? "mp4" : "jpg")}",
                    ContentType = type == AssetTypeEnum.VIDEO ? "video/mp4" : "image/jpeg",
                    FileStream = new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }),
                });
            mock.Setup(l => l.SendWebhookNotification(It.IsAny<IWebhookNotification>())).Returns(Task.CompletedTask);
            return mock;
        }

        private void SetupSearchAndThumbnail(AssetResponseDto asset)
        {
            var jsonResponse = JsonSerializer.Serialize(new SearchResponseDto
            {
                Assets = new SearchAssetResponseDto
                {
                    Count = 1,
                    Total = 1,
                    Items = { asset },
                    NextPage = null,
                },
            });
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/search/metadata")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() => new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(jsonResponse)
                });
            _mockHttpMessageHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("/thumbnail")),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(() => new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })
                });
        }

        // Builds a valid AssetResponseDto from the generated client types. Only the
        // fields required by the schema are set; pass type/duration to reuse this for
        // video assets. Changes to the OpenAPI schema surface here as compile errors.
        private static AssetResponseDto BuildAssetResponse(
            Guid id,
            AssetTypeEnum type = AssetTypeEnum.IMAGE,
            int? duration = null)
        {
            var timestamp = DateTimeOffset.Parse("2023-10-26T10:00:00Z");
            return new AssetResponseDto
            {
                Id = id,
                OwnerId = Guid.NewGuid(),
                Type = type,
                OriginalPath = "/path/to/image.jpg",
                OriginalFileName = "image.jpg",
                Checksum = "testchecksum",
                Thumbhash = "I0cMCQS94XmImZeXmYd3d3g=",
                Visibility = AssetVisibility.Timeline,
                Duration = duration,
                Width = 1920,
                Height = 1080,
                IsFavorite = true,
                HasMetadata = true,
                FileCreatedAt = timestamp,
                FileModifiedAt = timestamp,
                LocalDateTime = timestamp,
                CreatedAt = timestamp,
                UpdatedAt = timestamp,
            };
        }

        // TODO: Fix Test
        // [Test]
        // public async Task GetImage_VideoAsset_ReturnsVideoStream()
        // {
        //     // Arrange
        //     var videoAssetId = Guid.NewGuid();
        //     var assetInfoJson = $@"
        //     {{
        //         ""id"": ""{videoAssetId}"",
        //         ""originalFileName"": ""test-video.mp4"",
        //         ""type"": ""VIDEO"",
        //         ""fileCreatedAt"": ""2023-10-26T10:00:00Z"",
        //         ""fileModifiedAt"": ""2023-10-26T10:00:00Z"",
        //         ""duration"": ""0:00:05"",
        //         ""checksum"": ""checksum"",
        //         ""deviceAssetId"": ""deviceAsset"",
        //         ""deviceId"": ""device"",
        //         ""ownerId"": ""owner"",
        //         ""localDateTime"": ""2023-10-26T10:00:00Z"",
        //         ""visibility"": ""timeline"",
        //         ""hasMetadata"": true,
        //         ""isArchived"": false,
        //         ""isOffline"": false,
        //         ""isTrashed"": false,
        //         ""updatedAt"": ""2023-10-26T10:00:00Z""
        //     }}";

        //     _mockHttpMessageHandler.Protected()
        //         .Setup<Task<HttpResponseMessage>>(
        //             "SendAsync",
        //             ItExpr.Is<HttpRequestMessage>(req =>
        //                 req.Method == HttpMethod.Get &&
        //                 req.RequestUri!.ToString().EndsWith($"/assets/{videoAssetId}", StringComparison.OrdinalIgnoreCase)),
        //             ItExpr.IsAny<CancellationToken>()
        //         )
        //         .ReturnsAsync(() => new HttpResponseMessage
        //         {
        //             StatusCode = HttpStatusCode.OK,
        //             Content = new StringContent(assetInfoJson, Encoding.UTF8, "application/json")
        //         });

        //     var videoBytes = new byte[] { 0x00, 0x00, 0x00, 0x20 };
        //     _mockHttpMessageHandler.Protected()
        //         .Setup<Task<HttpResponseMessage>>(
        //             "SendAsync",
        //             ItExpr.Is<HttpRequestMessage>(req =>
        //                 req.Method == HttpMethod.Get &&
        //                 req.RequestUri!.ToString().Contains($"/assets/{videoAssetId}/video/playback", StringComparison.OrdinalIgnoreCase)),
        //             ItExpr.IsAny<CancellationToken>()
        //         )
        //         .ReturnsAsync(() =>
        //         {
        //             var response = new HttpResponseMessage
        //             {
        //                 StatusCode = HttpStatusCode.OK,
        //                 Content = new ByteArrayContent(videoBytes)
        //             };
        //             response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        //             return response;
        //         });

        //     var client = _factory.CreateClient();

        //     // Act
        //     var response = await client.GetAsync($"/api/Asset/{videoAssetId}/Asset?assetType=1");

        //     // Assert
        //     response.EnsureSuccessStatusCode();
        //     Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("video/mp4"));
        //     var resultBytes = await response.Content.ReadAsByteArrayAsync();
        //     Assert.That(resultBytes, Is.EqualTo(videoBytes));
        // }
    }
}
