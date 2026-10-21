using Moq;
using NUnit.Framework;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic.AccountSelection;

namespace ImmichFrame.Core.Tests.Logic.AccountSelection
{
    [TestFixture]
    public class AssetAccountTrackerTests
    {
        private AssetAccountTracker _tracker;
        private IAccountImmichFrameLogic _account1;
        private IAccountImmichFrameLogic _account2;

        [SetUp]
        public void Setup()
        {
            _tracker = new AssetAccountTracker();
            _account1 = Mock.Of<IAccountImmichFrameLogic>();
            _account2 = Mock.Of<IAccountImmichFrameLogic>();
        }

        [Test]
        public async Task GetAccount_ReturnsTheRecordedAccount()
        {
            var asset1 = Guid.NewGuid();
            var asset2 = Guid.NewGuid();
            await _tracker.RecordAssetLocation(_account1, asset1);
            await _tracker.RecordAssetLocation(_account2, asset2);

            Assert.Multiple(() =>
            {
                Assert.That(_tracker.GetAccount(asset1), Is.SameAs(_account1));
                Assert.That(_tracker.GetAccount(asset2), Is.SameAs(_account2));
                Assert.That(_tracker.GetAccount(Guid.NewGuid()), Is.Null);
            });
        }

        [Test]
        public async Task RecordAssetLocation_ReportsWhetherTheAccountChanged()
        {
            var asset = Guid.NewGuid();

            Assert.That(await _tracker.RecordAssetLocation(_account1, asset), Is.True);
            Assert.That(await _tracker.RecordAssetLocation(_account1, asset), Is.False);
            Assert.That(await _tracker.RecordAssetLocation(_account2, asset), Is.True);
            Assert.That(_tracker.GetAccount(asset), Is.SameAs(_account2));
        }

        [Test]
        public async Task RecordAssetLocation_DoesNotAskTheAccountForItsSize()
        {
            // The Bloom filter version sized itself from GetTotalAssets, which threw for an empty
            // account and hit Immich at record time.
            var account = new Mock<IAccountImmichFrameLogic>(MockBehavior.Strict);

            await _tracker.RecordAssetLocation(account.Object, Guid.NewGuid());

            Assert.That(_tracker.GetAccount(Guid.NewGuid()), Is.Null);
        }

        [Test]
        public async Task ManyAssets_HaveNoFalsePositives()
        {
            var recorded = Enumerable.Range(0, 10_000).Select(_ => Guid.NewGuid()).ToList();
            foreach (var id in recorded) await _tracker.RecordAssetLocation(_account1, id);

            Assert.That(recorded.All(id => _tracker.GetAccount(id) == _account1), Is.True);
            Assert.That(Enumerable.Range(0, 10_000).Any(_ => _tracker.GetAccount(Guid.NewGuid()) != null), Is.False);
        }

        [Test]
        public async Task Forget_RemovesOnlyTheRetiredAccountsAssets()
        {
            var retiredAsset = Guid.NewGuid();
            var keptAsset = Guid.NewGuid();
            var reRecordedAsset = Guid.NewGuid();
            await _tracker.RecordAssetLocation(_account1, retiredAsset);
            await _tracker.RecordAssetLocation(_account2, keptAsset);
            await _tracker.RecordAssetLocation(_account1, reRecordedAsset);
            await _tracker.RecordAssetLocation(_account2, reRecordedAsset);

            _tracker.Forget([_account1]);

            Assert.Multiple(() =>
            {
                Assert.That(_tracker.GetAccount(retiredAsset), Is.Null);
                Assert.That(_tracker.GetAccount(keptAsset), Is.SameAs(_account2));
                Assert.That(_tracker.GetAccount(reRecordedAsset), Is.SameAs(_account2));
            });
        }
    }
}
