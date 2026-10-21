using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;
using ImmichFrame.WebApi.Models;
using ImmichFrame.WebApi.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Helpers;

[TestFixture]
public class ImmichServerVersionCheckerTests
{
    private static AccountProbeResult Ok(string url) => new(url, true, "3.0.0", true, null);
    private static AccountProbeResult Unreachable(string url) => new(url, false, null, false, "Could not connect.");
    private static AccountProbeResult TooOld(string url) => new(url, true, "2.9.0", false, "too old");

    [Test]
    public void StartupAllowed_UnreachableServer_DoesNotBlockStartup()
    {
        Assert.That(ImmichServerVersionChecker.StartupAllowed([Ok("a"), Unreachable("b")]), Is.True);
    }

    [Test]
    public void StartupAllowed_ReachableServerTooOld_BlocksStartup()
    {
        Assert.That(ImmichServerVersionChecker.StartupAllowed([Ok("a"), TooOld("b")]), Is.False);
    }

    [Test]
    public void StartupAllowed_NoAccounts_Allows()
    {
        Assert.That(ImmichServerVersionChecker.StartupAllowed([]), Is.True);
    }

    [Test]
    public async Task CheckServerVersions_UsesProbeAndAllowsUnreachable()
    {
        var account = new ServerAccountSettings { ImmichServerUrl = "http://down.example", ApiKey = "k" };
        var settings = new ServerSettings { AccountsImpl = [account] };
        var probe = new Mock<IImmichServerProbe>();
        probe.Setup(p => p.ProbeAll(It.IsAny<IEnumerable<IAccountSettings>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Unreachable(account.ImmichServerUrl)]);

        var services = new ServiceCollection()
            .AddSingleton<IServerSettings>(settings)
            .AddSingleton(probe.Object)
            .BuildServiceProvider();

        var allowed = await ImmichServerVersionChecker.CheckServerVersions(services, NullLogger.Instance);

        Assert.That(allowed, Is.True);
        probe.Verify(p => p.ProbeAll(It.Is<IEnumerable<IAccountSettings>>(a => a.Single() == account),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
