using System.Net;
using System.Net.Sockets;
using ImmichFrame.WebApi.Models;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Helpers;

[TestFixture]
public class WebhookHelperTests
{
    [Test]
    public async Task SendWebhookNotification_UnreachableUrl_LogsInsteadOfThrowing()
    {
        // Reserve a free port, then close it so the connection is refused.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var logger = new Mock<ILogger>();
        var notification = new AssetRequestedNotification(Guid.NewGuid(), "test");

        await WebhookHelper.SendWebhookNotification(notification, $"http://127.0.0.1:{port}/hook", logger.Object);

        logger.Verify(l => l.Log(LogLevel.Warning, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Test]
    public async Task SendWebhookNotification_NoUrl_DoesNothing()
    {
        var logger = new Mock<ILogger>();

        await WebhookHelper.SendWebhookNotification(new AssetRequestedNotification(Guid.NewGuid(), "test"), null, logger.Object);

        logger.VerifyNoOtherCalls();
    }
}
