using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Settings;

namespace ImmichFrame.WebApi.Helpers
{
    public static class ImmichServerVersionChecker
    {
        /// <summary>
        /// The minimum Immich server major version supported by this version of ImmichFrame.
        /// </summary>
        public const int MinimumSupportedMajorVersion = 3;

        /// <summary>
        /// Checks and logs the version of every configured Immich server.
        /// </summary>
        /// <returns>
        /// <c>false</c> if the configuration could not be loaded or a reachable server reported a
        /// version older than v<see cref="MinimumSupportedMajorVersion"/>; ImmichFrame must not start
        /// against an API it does not speak. An unreachable server only logs: it may simply be
        /// starting up alongside ImmichFrame, and account selection skips it until it answers.
        /// </returns>
        public static async Task<bool> CheckServerVersions(IServiceProvider services, ILogger logger)
        {
            IEnumerable<IAccountSettings> accounts;
            try
            {
                accounts = services.GetRequiredService<IServerSettings>().Accounts.ToList();
            }
            catch (Exception ex)
            {
                logger.LogCritical("Could not check Immich server versions, config could not be loaded: {Message}", ex.Message);
                return false;
            }

            var results = await services.GetRequiredService<IImmichServerProbe>().ProbeAll(accounts);

            foreach (var result in results)
            {
                if (!result.Reachable)
                {
                    logger.LogError("Could not determine Immich server version for {Url}: {Message} ImmichFrame will keep retrying it while serving the other accounts.",
                        result.ImmichServerUrl, result.Error);
                }
                else if (!result.VersionSupported)
                {
                    logger.LogCritical("Immich server {Url} is running v{Version}, but this version of ImmichFrame requires Immich v{Minimum} or newer. Please update your Immich server.",
                        result.ImmichServerUrl, result.ServerVersion, MinimumSupportedMajorVersion);
                }
                else
                {
                    logger.LogInformation("Immich server {Url} is running v{Version}", result.ImmichServerUrl, result.ServerVersion);
                }
            }

            return StartupAllowed(results);
        }

        /// <summary>Only a server that answered with an unsupported version blocks startup.</summary>
        public static bool StartupAllowed(IEnumerable<AccountProbeResult> results) =>
            !results.Any(r => r.Reachable && !r.VersionSupported);
    }
}
