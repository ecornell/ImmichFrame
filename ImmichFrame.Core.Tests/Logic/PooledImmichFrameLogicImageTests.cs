using System.Net;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.Core.Tests.Logic;

[TestFixture]
public class PooledImmichFrameLogicImageTests
{
    private string _cacheDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "immichframe-image-cache-tests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_cacheDirectory)) Directory.Delete(_cacheDirectory, recursive: true);
    }

    [Test]
    public async Task UncachedImage_KeepsResponseOwnedUntilCallerDisposesIt()
    {
        var trackingStream = new TrackingStream([1, 2, 3]);
        var logic = CreateLogic(downloadImages: false, _ => ImageResponse(trackingStream));

        var asset = await logic.GetAsset(Guid.NewGuid(), AssetTypeEnum.IMAGE);

        Assert.That(trackingStream.Disposed, Is.False);
        asset.Owner!.Dispose();
        Assert.That(trackingStream.Disposed, Is.True);
    }

    [Test]
    public void FailedCacheWrite_DisposesResponseAndLeavesNoPartialFile()
    {
        var trackingStream = new ThrowingStream();
        var logic = CreateLogic(downloadImages: true, _ => ImageResponse(trackingStream));

        Assert.ThrowsAsync<IOException>(async () =>
            await logic.GetAsset(Guid.NewGuid(), AssetTypeEnum.IMAGE));

        Assert.That(trackingStream.Disposed, Is.True);
        Assert.That(Directory.Exists(_cacheDirectory)
            ? Directory.EnumerateFiles(_cacheDirectory).ToList()
            : [], Is.Empty);
    }

    [Test]
    public async Task ConcurrentRequestsForSameImage_WriteCacheOnceAndReturnCompleteFiles()
    {
        var requestCount = 0;
        var logic = CreateLogic(downloadImages: true, _ =>
        {
            Interlocked.Increment(ref requestCount);
            return ImageResponse(new TrackingStream([1, 2, 3, 4]));
        });
        var id = Guid.NewGuid();

        var assets = await Task.WhenAll(
            logic.GetAsset(id, AssetTypeEnum.IMAGE),
            logic.GetAsset(id, AssetTypeEnum.IMAGE));

        try
        {
            Assert.That(requestCount, Is.EqualTo(1));
            foreach (var asset in assets)
            {
                using var memory = new MemoryStream();
                await asset.FileStream.CopyToAsync(memory);
                Assert.That(memory.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            }

            Assert.That(Directory.EnumerateFiles(_cacheDirectory), Has.Exactly(1).Items);
            Assert.That(logic.ImageCacheLockCount, Is.Zero,
                "the keyed lock must be removed after all holders and waiters finish");
        }
        finally
        {
            foreach (var asset in assets) asset.FileStream.Dispose();
        }
    }

    [Test]
    public async Task CacheLocks_DoNotAccumulateAcrossDistinctAssets()
    {
        var logic = CreateLogic(downloadImages: true,
            _ => ImageResponse(new TrackingStream([1, 2, 3])));

        var assets = await Task.WhenAll(Enumerable.Range(0, 25)
            .Select(_ => logic.GetAsset(Guid.NewGuid(), AssetTypeEnum.IMAGE)));
        foreach (var asset in assets) asset.FileStream.Dispose();

        Assert.That(logic.ImageCacheLockCount, Is.Zero);
    }

    private PooledImmichFrameLogic CreateLogic(bool downloadImages,
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var handler = new DelegateHandler(responseFactory);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(x => x.CreateClient("ImmichApiAccountClient")).Returns(client);

        var general = new Mock<IGeneralSettings>();
        general.SetupGet(x => x.DownloadImages).Returns(downloadImages);
        general.SetupGet(x => x.RenewImagesDuration).Returns(30);
        general.SetupGet(x => x.RefreshAlbumPeopleInterval).Returns(1);

        var account = new Mock<IAccountSettings>();
        account.SetupGet(x => x.ImmichServerUrl).Returns("http://immich.test/api");
        account.SetupGet(x => x.ApiKey).Returns("test-key");

        return new PooledImmichFrameLogic(account.Object, general.Object, factory.Object, _cacheDirectory);
    }

    private static HttpResponseMessage ImageResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream)
        };
        response.Content.Headers.ContentType = new("image/jpeg");
        return response;
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }

    private sealed class TrackingStream(byte[] contents) : MemoryStream(contents)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingStream : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("simulated copy failure");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("simulated copy failure"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
