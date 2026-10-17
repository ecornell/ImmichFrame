using ImmichFrame.Core.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using System.Text.Encodings.Web;

namespace ImmichFrame.WebApi.Tests.Helpers;

[TestFixture]
public class ImmichFrameAuthenticationHandlerTests
{
    private const string Secret = "s3cret-token";

    private static async Task<AuthenticateResult> Authenticate(string? secret, string? authorizationHeader,
        bool endpointRequiresAuth = true)
    {
        var general = new Mock<IGeneralSettings>();
        general.SetupGet(g => g.AuthenticationSecret).Returns(secret);
        var settings = new Mock<IServerSettings>();
        settings.SetupGet(s => s.GeneralSettings).Returns(general.Object);

        var options = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());

        var handler = new ImmichFrameAuthenticationHandler(options.Object, NullLoggerFactory.Instance,
            UrlEncoder.Default, settings.Object);

        var context = new DefaultHttpContext();
        var metadata = endpointRequiresAuth ? new EndpointMetadataCollection(new AuthorizeAttribute()) : EndpointMetadataCollection.Empty;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));
        if (authorizationHeader != null)
            context.Request.Headers.Authorization = authorizationHeader;

        await handler.InitializeAsync(
            new AuthenticationScheme("ImmichFrameScheme", null, typeof(ImmichFrameAuthenticationHandler)), context);
        return await handler.AuthenticateAsync();
    }

    [Test]
    public async Task NoSecretConfigured_AllowsAnonymous() =>
        Assert.That((await Authenticate(null, null)).Succeeded, Is.True);

    [Test]
    public async Task EndpointWithoutAuthorize_AllowsAnonymous() =>
        Assert.That((await Authenticate(Secret, null, endpointRequiresAuth: false)).Succeeded, Is.True);

    [Test]
    public async Task MissingHeader_Fails() =>
        Assert.That((await Authenticate(Secret, null)).Succeeded, Is.False);

    [TestCase("Bearer wrong")]
    [TestCase("Bearer s3cret-toke")]
    [TestCase("Bearer s3cret-token-longer")]
    [TestCase("Basic s3cret-token")]
    public async Task WrongSecretOrScheme_Fails(string header) =>
        Assert.That((await Authenticate(Secret, header)).Succeeded, Is.False);

    [TestCase("Bearer s3cret-token")]
    [TestCase("bearer s3cret-token ")]
    public async Task CorrectSecret_Succeeds(string header) =>
        Assert.That((await Authenticate(Secret, header)).Succeeded, Is.True);
}
