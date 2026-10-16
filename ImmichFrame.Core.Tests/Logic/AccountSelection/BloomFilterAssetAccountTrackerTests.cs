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
    public class BloomFilterAssetAccountTrackerTests
    {
        private BloomFilterAssetAccountTracker _tracker;
        private Guid _assetId;
        private Mock<IAccountImmichFrameLogic> _account1;
        private Mock<IAccountImmichFrameLogic> _account2;

        [SetUp]
        public async Task Setup()
        {
            _tracker = new BloomFilterAssetAccountTracker(NullLogger<BloomFilterAssetAccountTracker>.Instance);
            _assetId = Guid.NewGuid();
            _account1 = CreateAccount();
            _account2 = CreateAccount();
            // Recording the id in both accounts reproduces a Bloom filter false positive, and
            // keeps the tests independent of the tracker's enumeration order.
            await _tracker.RecordAssetLocation(_account1.Object, _assetId);
            await _tracker.RecordAssetLocation(_account2.Object, _assetId);
        }

        private static Mock<IAccountImmichFrameLogic> CreateAccount()
        {
            var mock = new Mock<IAccountImmichFrameLogic>();
            mock.Setup(a => a.GetTotalAssets()).ReturnsAsync(1000);
            return mock;
        }

        private static ApiException ApiError(int statusCode)
            => new("error", statusCode, "", new Dictionary<string, IEnumerable<string>>(), null!);

        [Test]
        public async Task ForAssetAsync_AccountThrowsAsynchronously_FallsThroughToNextAccount()
        {
            // Whichever account is tried first is the false positive.
            var tried = new List<IAccountImmichFrameLogic>();
            var result = await _tracker.ForAssetAsync(_assetId, async logic =>
            {
                tried.Add(logic);
                await Task.Yield();
                if (tried.Count == 1) throw ApiError(400);
                return logic;
            });

            Assert.That(tried, Has.Count.EqualTo(2));
            Assert.That(result, Is.SameAs(tried[1]));
            Assert.That(tried, Is.EquivalentTo(new[] { _account1.Object, _account2.Object }));
        }

        [Test]
        public void ForAssetAsync_AllAccountsNotFound_ThrowsAssetNotFound()
        {
            Assert.ThrowsAsync<AssetNotFoundException>(() => _tracker.ForAssetAsync<string>(_assetId, async _ =>
            {
                await Task.Yield();
                throw ApiError(404);
            }));
        }

        [Test]
        public void ForAssetAsync_OtherApiError_Propagates()
        {
            var ex = Assert.ThrowsAsync<ApiException>(() => _tracker.ForAssetAsync<string>(_assetId, async _ =>
            {
                await Task.Yield();
                throw ApiError(416);
            }));

            Assert.That(ex!.StatusCode, Is.EqualTo(416));
        }
    }
}
