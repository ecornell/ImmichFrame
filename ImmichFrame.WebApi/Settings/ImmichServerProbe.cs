using ImmichFrame.Core.Api;
using ImmichFrame.Core.Helpers;
using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Helpers;

namespace ImmichFrame.WebApi.Settings;

public sealed record AccountProbeResult(
    string ImmichServerUrl,
    bool Reachable,
    string? ServerVersion,
    bool VersionSupported,
    string? Error)
{
    public bool Ok => Reachable && VersionSupported;
}

/// <summary>
/// Checks that an account's credentials actually reach a supported Immich server.
/// </summary>
public interface IImmichServerProbe
{
    Task<AccountProbeResult> Probe(IAccountSettings account, CancellationToken ct = default);

    Task<IReadOnlyList<AccountProbeResult>> ProbeAll(IEnumerable<IAccountSettings> accounts, CancellationToken ct = default);
}

/// <summary>
/// Shared by startup and the admin API so both apply the same compatibility rule.
/// </summary>
/// <remarks>
/// This type never terminates the process — deciding what an incompatible server means is the
/// caller's business. Startup still exits; a failed save just reports back to the user.
/// </remarks>
public sealed class ImmichServerProbe(IHttpClientFactory httpClientFactory) : IImmichServerProbe
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Bounds a save against several slow or unreachable servers.</summary>
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(15);

    public async Task<AccountProbeResult> Probe(IAccountSettings account, CancellationToken ct = default)
    {
        try
        {
            var httpClient = httpClientFactory.CreateClient("ImmichApiAccountClient");
            httpClient.UseApiKey(account.ApiKey);
            var immichApi = new ImmichApi(account.ImmichServerUrl, httpClient);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(RequestTimeout);

            var version = await immichApi.GetServerVersionAsync(cts.Token);
            var supported = version.Major >= ImmichServerVersionChecker.MinimumSupportedMajorVersion;

            return new AccountProbeResult(
                account.ImmichServerUrl,
                Reachable: true,
                ServerVersion: $"{version.Major}.{version.Minor}.{version.Patch}",
                VersionSupported: supported,
                Error: supported
                    ? null
                    : $"Immich v{version.Major}.{version.Minor}.{version.Patch} is older than the required v{ImmichServerVersionChecker.MinimumSupportedMajorVersion}.");
        }
        catch (Exception ex)
        {
            return new AccountProbeResult(account.ImmichServerUrl, false, null, false, ex.Message);
        }
    }

    public async Task<IReadOnlyList<AccountProbeResult>> ProbeAll(
        IEnumerable<IAccountSettings> accounts, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TotalTimeout);

        return await Task.WhenAll(accounts.Select(a => Probe(a, cts.Token)));
    }
}
