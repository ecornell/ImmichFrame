using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Models;
using ImmichFrame.WebApi.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Controllers;

[TestFixture]
public class AssetControllerDisposalTests
{
    [Test]
    public async Task RandomImageAndInfo_DisposesImageStreamAndResponseOwner()
    {
        var assetId = Guid.NewGuid();
        var imageStream = new TrackingStream([1, 2, 3]);
        var owner = new TrackingDisposable();
        var randomAsset = new AssetResponseDto
        {
            Id = assetId,
            Type = AssetTypeEnum.IMAGE,
            LocalDateTime = DateTimeOffset.UtcNow,
            Thumbhash = "I0cMCQS94XmImZeXmYd3d3g="
        };
        var logic = new Mock<IImmichFrameLogic>();
        logic.Setup(x => x.GetNextAsset()).ReturnsAsync(randomAsset);
        logic.Setup(x => x.GetAsset(assetId, AssetTypeEnum.IMAGE, null)).ReturnsAsync(new AssetResponse
        {
            FileName = "image.jpeg",
            ContentType = "image/jpeg",
            FileStream = imageStream,
            Owner = owner,
            IsPartial = false
        });
        var settings = new Mock<IGeneralSettings>();
        settings.SetupGet(x => x.Language).Returns("en");
        settings.SetupGet(x => x.PhotoDateFormat).Returns("yyyy-MM-dd");
        settings.SetupGet(x => x.ImageLocationFormat).Returns("City,State,Country");
        var controller = new AssetController(
            NullLogger<AssetController>.Instance, logic.Object, settings.Object,
            new MemoryCache(new MemoryCacheOptions()));

        var response = await controller.GetRandomImageAndInfo();

        Assert.That(response.Value!.RandomImageBase64, Is.EqualTo("AQID"));
        Assert.That(imageStream.Disposed, Is.True);
        Assert.That(owner.Disposed, Is.True);
    }

    [Test]
    public async Task GetAsset_ClientAbortsVideoRange_EndsQuietlyAndDisposesOwner()
    {
        var assetId = Guid.NewGuid();
        var owner = new TrackingDisposable();
        var logic = new Mock<IImmichFrameLogic>();
        logic.Setup(x => x.GetAsset(assetId, AssetTypeEnum.VIDEO, "bytes=0-")).ReturnsAsync(new AssetResponse
        {
            FileName = "video.mp4",
            ContentType = "video/mp4",
            FileStream = new TrackingStream([1, 2, 3]),
            Owner = owner,
            IsPartial = true,
            ContentRange = "bytes 0-2/3"
        });
        var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var httpContext = new DefaultHttpContext { RequestAborted = aborted.Token };
        httpContext.Request.Headers["Range"] = "bytes=0-";
        var body = new MemoryStream();
        httpContext.Response.Body = body;
        var controller = new AssetController(
            NullLogger<AssetController>.Instance, logic.Object, Mock.Of<IGeneralSettings>(),
            new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.GetAsset(assetId, assetType: AssetTypeEnum.VIDEO);

        Assert.That(result, Is.InstanceOf<EmptyResult>());
        Assert.That(body.Length, Is.Zero, "the copy should observe the aborted request");
        Assert.That(owner.Disposed, Is.True);
    }

    private sealed class TrackingStream(byte[] contents) : MemoryStream(contents)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
