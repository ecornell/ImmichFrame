using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Helpers;

[TestFixture]
public class CustomAuthenticationMiddlewareTests
{
    private sealed class ListLogger : ILogger<CustomAuthenticationMiddleware>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static (DefaultHttpContext Context, ListLogger Logger, bool NextCalled) Run(AuthenticateResult result,
        Action<HttpContext> configure)
    {
        var auth = new Mock<IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), "ImmichFrameScheme")).ReturnsAsync(result);

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider()
        };
        context.Response.Body = new MemoryStream();
        configure(context);

        var logger = new ListLogger();
        var nextCalled = false;
        var middleware = new CustomAuthenticationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, logger);

        middleware.InvokeAsync(context).GetAwaiter().GetResult();
        return (context, logger, nextCalled);
    }

    [Test]
    public void Rejection_LogsWhoWasRejected_WithoutQueryOrToken()
    {
        var (context, logger, nextCalled) = Run(AuthenticateResult.Fail("The AuthenticationSecret was not correct!"), c =>
        {
            c.Request.Method = "GET";
            c.Request.Path = "/api/Asset/RandomImageAndInfo";
            c.Request.QueryString = new QueryString("?clientIdentifier=Kitchen%20tablet&authsecret=leaked");
            c.Request.Headers.Authorization = "Bearer wrong-token";
            c.Request.Headers.UserAgent = "okhttp/4.12.0";
            c.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        });

        Assert.Multiple(() =>
        {
            Assert.That(nextCalled, Is.False);
            Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status401Unauthorized));
            var (level, message) = logger.Entries.Single();
            Assert.That(level, Is.EqualTo(LogLevel.Warning));
            Assert.That(message, Does.Contain("GET /api/Asset/RandomImageAndInfo from 192.168.1.50"));
            Assert.That(message, Does.Contain("client 'Kitchen tablet'"));
            Assert.That(message, Does.Contain("user agent 'okhttp/4.12.0'"));
            Assert.That(message, Does.EndWith("The AuthenticationSecret was not correct!"));
            Assert.That(message, Does.Not.Contain("leaked"));
            Assert.That(message, Does.Not.Contain("wrong-token"));
        });
    }

    [Test]
    public void Rejection_StripsControlCharacters_SoALogLineCannotBeForged()
    {
        var (_, logger, _) = Run(AuthenticateResult.Fail("Missing Authorization Header"), c =>
        {
            c.Request.Path = "/api/Asset";
            c.Request.Headers.UserAgent = "agent\r\nfake: log line";
        });

        var message = logger.Entries.Single().Message;
        Assert.That(message, Does.Not.Contain("\n").And.Not.Contain("\r"));
        Assert.That(message, Does.Contain("from unknown"));
    }

    [Test]
    public void Success_CallsNext_AndLogsNothing()
    {
        var ticket = new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(), "ImmichFrameScheme");
        var (_, logger, nextCalled) = Run(AuthenticateResult.Success(ticket), _ => { });

        Assert.That(nextCalled, Is.True);
        Assert.That(logger.Entries, Is.Empty);
    }
}
