using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic;
using ImmichFrame.Core.Logic.Pool;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;

namespace ImmichFrame.WebApi.Tests.Helpers;

[TestFixture]
public class AssetShowHistoryWiringTests
{
    [Test]
    public void AccountLogicResolvesTheHistoryFromDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<AssetShowHistory>();
        services.AddSingleton<IAssetShowHistory>(srv => srv.GetRequiredService<AssetShowHistory>());
        using var provider = services.BuildServiceProvider();
        var account = new Mock<IAccountSettings>();
        account.SetupGet(x => x.ImmichServerUrl).Returns("http://immich.invalid");
        account.SetupGet(x => x.ApiKey).Returns("key");

        // Same call the account factory in Program.cs and ImmichFrameLogicFactory make.
        var logic = ActivatorUtilities.CreateInstance<PooledImmichFrameLogic>(
            provider, account.Object, Mock.Of<IGeneralSettings>());

        Assert.That(logic, Is.Not.Null);
    }

    [Test]
    public void AccountLogicResolvesWithARegisteredImageCacheLocation()
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddSingleton<IAssetShowHistory, AssetShowHistory>();
        services.AddSingleton(new ImageCacheLocation(Path.Combine(Path.GetTempPath(), "immichframe-cache-wiring")));
        using var provider = services.BuildServiceProvider();
        var account = new Mock<IAccountSettings>();
        account.SetupGet(x => x.ImmichServerUrl).Returns("http://immich.invalid");
        account.SetupGet(x => x.ApiKey).Returns("key");

        var logic = ActivatorUtilities.CreateInstance<PooledImmichFrameLogic>(
            provider, account.Object, Mock.Of<IGeneralSettings>());

        Assert.That(logic, Is.Not.Null);
    }
}
