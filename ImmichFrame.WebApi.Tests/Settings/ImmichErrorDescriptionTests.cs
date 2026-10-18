using System.Net;
using System.Net.Http;
using ImmichFrame.Core.Api;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Settings;

[TestFixture]
public class ImmichErrorDescriptionTests
{
    private const string InternalPage = "<html>internal-admin-dashboard secret=hunter2</html>";

    private static ApiException ApiError(int status) =>
        new("The HTTP status code of the response was not expected.", status, InternalPage,
            new Dictionary<string, IEnumerable<string>>(), null!);

    [TestCase(401, "rejected the API key")]
    [TestCase(403, "rejected the API key")]
    [TestCase(404, "HTTP 404")]
    [TestCase(500, "HTTP 500")]
    public void ApiErrors_NameTheStatusButNotTheBody(int status, string expected)
    {
        var message = ImmichErrorDescription.Describe(ApiError(status));

        Assert.That(message, Does.Contain(expected));
        Assert.That(message, Does.Not.Contain("internal-admin-dashboard"));
    }

    [Test]
    public void TransportErrors_AreDescribedGenerically()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ImmichErrorDescription.Describe(
                    new HttpRequestException(HttpRequestError.NameResolutionError, "no such host 10.0.0.5")),
                Does.Contain("host name").And.Not.Contain("10.0.0.5"));
            Assert.That(ImmichErrorDescription.Describe(new HttpRequestException("connection refused")),
                Is.EqualTo("Could not connect to the Immich server."));
            Assert.That(ImmichErrorDescription.Describe(new TaskCanceledException()), Does.Contain("in time"));
            Assert.That(ImmichErrorDescription.Describe(new Exception("anything")),
                Is.EqualTo("The request to the Immich server failed."));
        });
    }

    [Test]
    public async Task Probe_DoesNotEchoTheResponseBody()
    {
        var handler = new StubHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(InternalPage)
        });
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler));
        var probe = new ImmichServerProbe(factory.Object, NullLogger<ImmichServerProbe>.Instance);

        var result = await probe.Probe(new ServerAccountSettings
        {
            ImmichServerUrl = "http://internal.example:8080",
            ApiKey = "key"
        });

        Assert.That(result.Reachable, Is.False);
        Assert.That(result.Error, Is.EqualTo("The Immich server returned HTTP 500."));
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(response);
    }
}
