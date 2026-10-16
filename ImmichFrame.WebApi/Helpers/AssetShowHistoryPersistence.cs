using ImmichFrame.Core.Logic.Pool;
using ImmichFrame.WebApi.Settings;

namespace ImmichFrame.WebApi.Helpers;

/// <summary>
/// Loads the shown-asset history from the config directory at startup and saves it periodically
/// and at shutdown, so a restart or redeploy does not make recently shown assets eligible again.
/// </summary>
public sealed class AssetShowHistoryPersistence(
    AssetShowHistory history,
    ConfigLocation configLocation,
    ILogger<AssetShowHistoryPersistence> logger) : BackgroundService
{
    internal const string FileName = "ShownAssets.json";
    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    private string? _path;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!configLocation.CanPersist())
        {
            logger.LogWarning("Config directory {Directory} is not writable; shown-asset history will reset on restart",
                configLocation.Directory);
            return Task.CompletedTask;
        }

        _path = Path.Combine(configLocation.Directory, FileName);
        try
        {
            history.Load(_path);
        }
        catch (Exception ex)
        {
            // A damaged history only costs repeat avoidance; it must not stop the frame.
            logger.LogWarning(ex, "Ignoring unreadable shown-asset history {Path}", _path);
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SaveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) Save();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        Save();
    }

    private void Save()
    {
        if (_path == null) return;
        try
        {
            history.Save(_path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save shown-asset history {Path}", _path);
        }
    }
}
