using NUnit.Framework;
using Moq;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.Pool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

namespace ImmichFrame.Core.Tests.Logic.Pool;

[TestFixture]
public class FavoriteAssetsPoolTests
{
    private Mock<IApiCache> _mockApiCache;
    private Mock<ImmichApi> _mockImmichApi;
    private Mock<IAccountSettings> _mockAccountSettings; // Though not directly used by LoadAssets here
    private TestableFavoriteAssetsPool _favoriteAssetsPool;

    private class TestableFavoriteAssetsPool : FavoriteAssetsPool
    {
        public TestableFavoriteAssetsPool(IApiCache apiCache, ImmichApi immichApi, IAccountSettings accountSettings)
            : base(apiCache, immichApi, accountSettings) { }

        public Task<IEnumerable<AssetResponseDto>> TestLoadAssets(CancellationToken ct = default)
        {
            return base.LoadAssets(ct);
        }
    }

    [SetUp]
    public void Setup()
    {
        _mockApiCache = new Mock<IApiCache>();
        _mockImmichApi = new Mock<ImmichApi>(null, null);
        _mockAccountSettings = new Mock<IAccountSettings>();
        _favoriteAssetsPool = new TestableFavoriteAssetsPool(_mockApiCache.Object, _mockImmichApi.Object, _mockAccountSettings.Object);
    }

    private AssetResponseDto CreateAsset(string id, AssetTypeEnum type = AssetTypeEnum.IMAGE) => new AssetResponseDto { Id = FixtureHelpers.GuidFor(id), Type = type };
    private SearchResponseDto CreateSearchResult(List<AssetResponseDto> assets, int total,
        string? nextPage = null) => new()
        { Assets = new SearchAssetResponseDto { Items = assets, Total = total, NextPage = nextPage } };

    [Test]
    public async Task LoadAssets_CallsSearchAssetsAsync_WithFavoriteTrue_AndPaginates()
    {
        // Arrange
        var batchSize = 1000; // From FavoriteAssetsPool.cs
        var assetsPage1 = Enumerable.Range(0, batchSize).Select(i => CreateAsset($"fav_p1_{i}")).ToList();
        var assetsPage2 = Enumerable.Range(0, 30).Select(i => CreateAsset($"fav_p2_{i}")).ToList();
        const int globalTotal = 1030;

        _mockImmichApi.SetupSequence(api => api.SearchAssetsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(assetsPage1, globalTotal, "2"))
            .ReturnsAsync(CreateSearchResult(assetsPage2, globalTotal));

        // Act
        var result = (await _favoriteAssetsPool.TestLoadAssets()).ToList();

        // Assert
        Assert.That(result.Count, Is.EqualTo(globalTotal));
        Assert.That(result.Any(a => a.Id == FixtureHelpers.GuidFor("fav_p1_0")));
        Assert.That(result.Any(a => a.Id == FixtureHelpers.GuidFor("fav_p2_29")));

        _mockImmichApi.Verify(api => api.SearchAssetsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.Is<MetadataSearchDto>(dto =>
                dto.IsFavorite == true &&
                dto.Type == AssetTypeEnum.IMAGE &&
                dto.WithExif == true &&
                dto.WithPeople == true &&
                dto.Page == 1 && dto.Size == batchSize),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockImmichApi.Verify(api => api.SearchAssetsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.Is<MetadataSearchDto>(dto =>
                dto.IsFavorite == true &&
                dto.Type == AssetTypeEnum.IMAGE &&
                dto.Page == 2 && dto.Size == batchSize),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoadAssets_StopsWhenNextPageIsAbsentEvenForAFullPage()
    {
        var assets = Enumerable.Range(0, 1000).Select(i => CreateAsset($"fav_{i}")).ToList();
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(assets, 1000));

        var result = (await _favoriteAssetsPool.TestLoadAssets()).ToList();

        Assert.That(result, Has.Count.EqualTo(1000));
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoadAssets_FollowsNextPageAfterShortPage()
    {
        _mockImmichApi.SetupSequence(api => api.SearchAssetsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult([CreateAsset("first")], 2, "2"))
            .ReturnsAsync(CreateSearchResult([CreateAsset("second")], 2));

        var result = (await _favoriteAssetsPool.TestLoadAssets()).ToList();

        Assert.That(result, Has.Count.EqualTo(2));
        _mockImmichApi.Verify(api => api.SearchAssetsAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.Is<MetadataSearchDto>(dto => dto.Page == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task LoadAssets_HandlesEmptyFavorites()
    {
        _mockImmichApi.Setup(api => api.SearchAssetsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<MetadataSearchDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSearchResult(new List<AssetResponseDto>(), 0));

        var result = (await _favoriteAssetsPool.TestLoadAssets()).ToList();
        Assert.That(result, Is.Empty);
    }
}
