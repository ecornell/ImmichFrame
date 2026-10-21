using Moq;
using NUnit.Framework;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Exceptions;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.AccountSelection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImmichFrame.Core.Tests.Logic.AccountSelection
{
    [TestFixture]
    public class TotalAccountImagesSelectionStrategyTests
    {
        private Mock<IAssetAccountTracker> _mockTracker;

        [SetUp]
        public void Setup()
        {
            _mockTracker = new Mock<IAssetAccountTracker>();
            _mockTracker
                .Setup(t => t.RecordAssetLocation(It.IsAny<IAccountImmichFrameLogic>(), It.IsAny<Guid>()))
                .ReturnsAsync(true);
        }

        private TotalAccountImagesSelectionStrategy CreateStrategy(params IAccountImmichFrameLogic[] accounts)
            => new(NullLogger<TotalAccountImagesSelectionStrategy>.Instance, _mockTracker.Object, accounts.ToList());

        private static Mock<IAccountImmichFrameLogic> CreateAccount(long totalAssets, IEnumerable<AssetResponseDto> assets)
        {
            var mock = new Mock<IAccountImmichFrameLogic>();
            mock.Setup(a => a.GetTotalAssets()).ReturnsAsync(totalAssets);
            mock.Setup(a => a.GetAssets()).ReturnsAsync(assets);
            return mock;
        }

        private static AssetResponseDto CreateAsset() => new()
        {
            Id = Guid.NewGuid(),
            OriginalPath = "/path/asset.jpg",
            Type = AssetTypeEnum.IMAGE,
        };

        [Test]
        public async Task GetAssets_NoAccounts_ReturnsEmpty()
        {
            var strategy = CreateStrategy();

            var assets = await strategy.GetAssets();

            Assert.That(assets, Is.Empty);
        }

        [Test]
        public async Task GetAssets_AllAccountsEmpty_ReturnsEmptyWithoutNaN()
        {
            var account1 = CreateAccount(0, Enumerable.Empty<AssetResponseDto>());
            var account2 = CreateAccount(0, Enumerable.Empty<AssetResponseDto>());
            var strategy = CreateStrategy(account1.Object, account2.Object);

            var assets = await strategy.GetAssets();

            Assert.That(assets, Is.Empty);
        }

        [Test]
        public async Task GetAssets_UnevenAccounts_LargestAccountKeepsAllAssets()
        {
            var largeAssets = Enumerable.Range(0, 10).Select(_ => CreateAsset()).ToList();
            var smallAssets = Enumerable.Range(0, 10).Select(_ => CreateAsset()).ToList();
            var largeAccount = CreateAccount(1000, largeAssets);
            var smallAccount = CreateAccount(100, smallAssets);
            var strategy = CreateStrategy(largeAccount.Object, smallAccount.Object);

            var result = (await strategy.GetAssets()).ToList();

            var fromLarge = result.Count(t => ReferenceEquals(t.Item1, largeAccount.Object));
            var fromSmall = result.Count(t => ReferenceEquals(t.Item1, smallAccount.Object));
            Assert.Multiple(() =>
            {
                // The account with the most assets keeps its full batch, smaller ones are scaled.
                Assert.That(fromLarge, Is.EqualTo(10));
                Assert.That(fromSmall, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task GetAssets_RecordsEveryReturnedAssetWithTracker()
        {
            var assets = Enumerable.Range(0, 5).Select(_ => CreateAsset()).ToList();
            var account = CreateAccount(5, assets);
            var strategy = CreateStrategy(account.Object);

            var result = (await strategy.GetAssets()).ToList();

            Assert.That(result, Has.Count.EqualTo(5));
            _mockTracker.Verify(
                t => t.RecordAssetLocation(account.Object, It.IsAny<Guid>()),
                Times.Exactly(5));
        }

        [Test]
        public async Task GetNextAsset_ReturnsAssetAndRecordsLocation()
        {
            var asset = CreateAsset();
            var account = CreateAccount(1, new[] { asset });
            account.Setup(a => a.GetNextAsset()).ReturnsAsync(asset);
            var strategy = CreateStrategy(account.Object);

            var result = await strategy.GetNextAsset();

            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Value.Item2.Id, Is.EqualTo(asset.Id));
            _mockTracker.Verify(t => t.RecordAssetLocation(account.Object, asset.Id), Times.Once);
        }

        [Test]
        public async Task GetNextAsset_NoAccounts_ReturnsNull()
        {
            var strategy = CreateStrategy();

            var result = await strategy.GetNextAsset();

            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task GetNextAsset_AllAccountsEmpty_ReturnsNull()
        {
            var account1 = CreateAccount(0, []);
            var account2 = CreateAccount(0, []);
            var strategy = CreateStrategy(account1.Object, account2.Object);

            var result = await strategy.GetNextAsset();

            Assert.That(result, Is.Null);
            account1.Verify(a => a.GetNextAsset(), Times.Never);
            account2.Verify(a => a.GetNextAsset(), Times.Never);
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
            public override DateTimeOffset GetUtcNow() => Now;
        }

        [Test]
        public async Task GetNextAsset_AccountCountFails_OtherAccountsKeepServing()
        {
            var time = new ManualTimeProvider();
            var asset = CreateAsset();
            var healthy = CreateAccount(10, [asset]);
            healthy.Setup(a => a.GetNextAsset()).ReturnsAsync(asset);
            var failing = new Mock<IAccountImmichFrameLogic>();
            failing.Setup(a => a.GetTotalAssets()).ThrowsAsync(new HttpRequestException("down"));
            var strategy = new TotalAccountImagesSelectionStrategy(NullLogger<TotalAccountImagesSelectionStrategy>.Instance,
                _mockTracker.Object, [failing.Object, healthy.Object], time);

            for (var i = 0; i < 5; i++)
            {
                var result = await strategy.GetNextAsset();
                Assert.That(result!.Value.Item1, Is.SameAs(healthy.Object));
            }

            // Cooled down: not hammered on every pick.
            failing.Verify(a => a.GetTotalAssets(), Times.Once);
            failing.Verify(a => a.GetNextAsset(), Times.Never);

            // Retried after the cooldown, and used again once it answers.
            var recovered = CreateAsset();
            failing.Setup(a => a.GetTotalAssets()).ReturnsAsync(1_000_000);
            failing.Setup(a => a.GetNextAsset()).ReturnsAsync(recovered);
            time.Now += TotalAccountImagesSelectionStrategy.FailureCooldown + TimeSpan.FromSeconds(1);

            var afterRecovery = await strategy.GetNextAsset();
            failing.Verify(a => a.GetTotalAssets(), Times.Exactly(2));
            Assert.That(afterRecovery, Is.Not.Null);
        }

        [Test]
        public async Task GetNextAsset_AccountHangs_OtherAccountsServeWithoutWaitingItOut()
        {
            // A black-holed server never answers; the HttpClient default would wait 100 s.
            var asset = CreateAsset();
            var healthy = CreateAccount(10, [asset]);
            healthy.Setup(a => a.GetNextAsset()).ReturnsAsync(asset);
            var hanging = new Mock<IAccountImmichFrameLogic>();
            hanging.Setup(a => a.GetTotalAssets()).Returns(new TaskCompletionSource<long>().Task);
            var strategy = new TotalAccountImagesSelectionStrategy(NullLogger<TotalAccountImagesSelectionStrategy>.Instance,
                _mockTracker.Object, [hanging.Object, healthy.Object], TimeProvider.System, TimeSpan.FromMilliseconds(100));

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var first = await strategy.GetNextAsset();
            var second = await strategy.GetNextAsset();
            stopwatch.Stop();

            Assert.That(first!.Value.Item1, Is.SameAs(healthy.Object));
            Assert.That(second!.Value.Item1, Is.SameAs(healthy.Object));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
            // Cooled down after the first timeout, so the second pick did not wait again.
            hanging.Verify(a => a.GetTotalAssets(), Times.Once);
        }

        [Test]
        public async Task GetNextAsset_ChosenAccountFails_RetriesWithAnotherAccount()
        {
            var asset = CreateAsset();
            var healthy = CreateAccount(1, [asset]);
            healthy.Setup(a => a.GetNextAsset()).ReturnsAsync(asset);
            // Its count is cached, so it is chosen, but fetching the asset fails.
            var failing = CreateAccount(1_000_000_000, []);
            failing.Setup(a => a.GetNextAsset()).ThrowsAsync(new HttpRequestException("down"));
            var strategy = CreateStrategy(failing.Object, healthy.Object);

            var result = await strategy.GetNextAsset();

            Assert.That(result!.Value.Item1, Is.SameAs(healthy.Object));
        }

        [Test]
        public void GetNextAsset_OnlyAccountFailsToFetch_Rethrows()
        {
            var failing = CreateAccount(10, []);
            failing.Setup(a => a.GetNextAsset()).ThrowsAsync(new HttpRequestException("down"));
            var strategy = CreateStrategy(failing.Object);

            Assert.ThrowsAsync<HttpRequestException>(() => strategy.GetNextAsset());
        }

        [Test]
        public async Task GetAssets_FailingAccount_ReturnsOtherAccountsAssets()
        {
            var assets = Enumerable.Range(0, 3).Select(_ => CreateAsset()).ToList();
            var healthy = CreateAccount(3, assets);
            var failing = new Mock<IAccountImmichFrameLogic>();
            failing.Setup(a => a.GetTotalAssets()).ThrowsAsync(new HttpRequestException("down"));
            failing.Setup(a => a.GetAssets()).ThrowsAsync(new HttpRequestException("down"));
            var strategy = CreateStrategy(failing.Object, healthy.Object);

            var result = (await strategy.GetAssets()).ToList();

            Assert.That(result.Select(r => r.Item2.Id), Is.EquivalentTo(assets.Select(a => a.Id)));
        }

        private static ApiException ApiError(int statusCode)
            => new("error", statusCode, "", new Dictionary<string, IEnumerable<string>>(), null!);

        private void TrackerKnowsNothing() =>
            _mockTracker.Setup(t => t.GetAccount(It.IsAny<Guid>())).Returns((IAccountImmichFrameLogic?)null);

        private void TrackerKnows(Guid assetId, IAccountImmichFrameLogic account) =>
            _mockTracker.Setup(t => t.GetAccount(assetId)).Returns(account);

        [Test]
        public async Task ForAssetAsync_TrackedAsset_AsksTheTrackedAccountFirst()
        {
            var assetId = Guid.NewGuid();
            var other = CreateAccount(10, []);
            var owner = CreateAccount(10, []);
            TrackerKnows(assetId, owner.Object);
            var strategy = CreateStrategy(other.Object, owner.Object);
            var asked = new List<IAccountImmichFrameLogic>();

            var result = await strategy.ForAssetAsync(assetId, logic =>
            {
                asked.Add(logic);
                return Task.FromResult("found");
            });

            Assert.That(result, Is.EqualTo("found"));
            Assert.That(asked, Is.EqualTo(new[] { owner.Object }));
        }

        [Test]
        public async Task ForAssetAsync_TrackedAccountFromRetiredGeneration_IsStillUsed()
        {
            // During the grace period after a settings save the on-screen asset belongs to an account
            // that is no longer in the current list.
            var assetId = Guid.NewGuid();
            var retired = CreateAccount(10, []);
            TrackerKnows(assetId, retired.Object);
            var strategy = CreateStrategy(CreateAccount(10, []).Object);

            var result = await strategy.ForAssetAsync(assetId,
                logic => logic == retired.Object ? Task.FromResult("found") : throw ApiError(404));

            Assert.That(result, Is.EqualTo("found"));
        }

        [TestCase(401)]
        [TestCase(403)]
        public async Task ForAssetAsync_TrackedAccountRejectsKey_TriesTheNextAccount(int status)
        {
            // E.g. two accounts on the same server, one key since revoked.
            var assetId = Guid.NewGuid();
            var revoked = CreateAccount(10, []);
            var working = CreateAccount(10, []);
            TrackerKnows(assetId, revoked.Object);
            var strategy = CreateStrategy(revoked.Object, working.Object);

            var result = await strategy.ForAssetAsync(assetId,
                logic => logic == revoked.Object ? throw ApiError(status) : Task.FromResult("found"));

            Assert.That(result, Is.EqualTo("found"));
            _mockTracker.Verify(t => t.RecordAssetLocation(working.Object, assetId), Times.Once);
        }

        [Test]
        public async Task ForAssetAsync_TrackedAccountUnreachable_TriesTheNextAccount()
        {
            var assetId = Guid.NewGuid();
            var down = CreateAccount(10, []);
            var up = CreateAccount(10, []);
            TrackerKnows(assetId, down.Object);
            var strategy = CreateStrategy(down.Object, up.Object);

            var result = await strategy.ForAssetAsync(assetId,
                logic => logic == down.Object ? throw new HttpRequestException("down") : Task.FromResult("found"));

            Assert.That(result, Is.EqualTo("found"));
        }

        [Test]
        public void ForAssetAsync_UnreachableAndNotFoundElsewhere_ReportsTheOutageNotA404()
        {
            var assetId = Guid.NewGuid();
            var down = CreateAccount(10, []);
            var other = CreateAccount(10, []);
            TrackerKnows(assetId, down.Object);
            var strategy = CreateStrategy(down.Object, other.Object);

            Assert.ThrowsAsync<HttpRequestException>(() => strategy.ForAssetAsync<string>(assetId,
                logic => logic == down.Object ? throw new HttpRequestException("down") : throw ApiError(404)));
        }

        [Test]
        public async Task ForAssetAsync_UntrackedAsset_AsksEachAccountAndRecordsTheOwner()
        {
            // E.g. an id the client queued before a restart.
            TrackerKnowsNothing();
            var assetId = Guid.NewGuid();
            var other = CreateAccount(10, []);
            var owner = CreateAccount(10, []);
            var strategy = CreateStrategy(other.Object, owner.Object);

            var result = await strategy.ForAssetAsync(assetId,
                logic => logic == other.Object ? throw ApiError(404) : Task.FromResult("found"));

            Assert.That(result, Is.EqualTo("found"));
            _mockTracker.Verify(t => t.RecordAssetLocation(owner.Object, assetId), Times.Once);
            _mockTracker.Verify(t => t.RecordAssetLocation(other.Object, assetId), Times.Never);
        }

        [Test]
        public void ForAssetAsync_UntrackedAndInNoAccount_Throws()
        {
            TrackerKnowsNothing();
            var strategy = CreateStrategy(CreateAccount(10, []).Object, CreateAccount(10, []).Object);

            Assert.ThrowsAsync<AssetNotFoundException>(() =>
                strategy.ForAssetAsync<string>(Guid.NewGuid(), _ => throw ApiError(400)));
        }

        [Test]
        public void ForAssetAsync_UntrackedAsset_OtherErrorsPropagate()
        {
            // A 500 (or 416 for a bad Range) is a real answer, not "try the next account".
            TrackerKnowsNothing();
            var strategy = CreateStrategy(CreateAccount(10, []).Object);

            var thrown = Assert.ThrowsAsync<ApiException>(() =>
                strategy.ForAssetAsync<string>(Guid.NewGuid(), _ => throw ApiError(500)));
            Assert.That(thrown!.StatusCode, Is.EqualTo(500));
        }
}
}
