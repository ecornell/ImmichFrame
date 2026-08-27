using ImmichFrame.Core.Interfaces;
using ImmichFrame.Core.Logic;
using ImmichFrame.Core.Logic.AccountSelection;
using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

public enum SaveStatus
{
    Applied,
    Invalid,
    Unreachable,
    Conflict,
    Busy,
    WriteFailed
}

public sealed record SaveOutcome(
    SaveStatus Status,
    long Version,
    IReadOnlyList<string> Problems,
    IReadOnlyList<AccountProbeResult> Probes,
    IReadOnlyList<string> Warnings)
{
    public static SaveOutcome Busy(long v) => new(SaveStatus.Busy, v, [], [], []);
    public static SaveOutcome Conflict(long v) => new(SaveStatus.Conflict, v, [], [], []);
    public static SaveOutcome Invalid(long v, IReadOnlyList<string> problems) => new(SaveStatus.Invalid, v, problems, [], []);
    public static SaveOutcome Unreachable(long v, IReadOnlyList<AccountProbeResult> probes) => new(SaveStatus.Unreachable, v, [], probes, []);
    public static SaveOutcome WriteFailed(long v, string problem) => new(SaveStatus.WriteFailed, v, [problem], [], []);
}

/// <summary>
/// Applies a candidate configuration: validate, persist, publish, refresh.
/// </summary>
public sealed class SettingsReloadService(
    SettingsStore store,
    SettingsFileWriter writer,
    ImmichFrameLogicFactory logicFactory,
    IImmichServerProbe probe,
    IAssetAccountTracker tracker,
    IEnumerable<ISettingsResettable> resettables,
    ConfigLocation location,
    ILogger<SettingsReloadService> logger)
{
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    /// <summary>
    /// How long a replaced generation's accounts stay resolvable in the tracker. Must comfortably
    /// exceed the slideshow interval so the image currently on screen can still be fetched.
    /// </summary>
    private static readonly TimeSpan RetirementGrace = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Applies <paramref name="candidate"/>.
    /// </summary>
    /// <remarks>
    /// Ordered so that everything which can fail happens before anything is mutated. In particular
    /// the replacement account graph is built <em>before</em> the file is written, which turns "the
    /// rebuild threw halfway through" into "the rebuild threw before we touched anything". Past the
    /// swap there are only reference assignments, which cannot throw.
    /// </remarks>
    public async Task<SaveOutcome> SaveAsync(
        ServerSettings candidate, long expectedVersion, bool force, CancellationToken ct = default)
    {
        if (!await _saveGate.WaitAsync(TimeSpan.FromSeconds(30), ct))
        {
            return SaveOutcome.Busy(store.Current.Version);
        }

        try
        {
            var previous = store.Current;

            // 1. Optimistic concurrency. Also guarantees the account indexes the caller sent refer to
            //    the list it actually saw, which is what makes "keep the existing API key" safe.
            if (expectedVersion != previous.Version)
            {
                return SaveOutcome.Conflict(previous.Version);
            }

            // 2. Structural validation (non-mutating, reports everything at once).
            if (!SettingsValidator.TryValidate(candidate, out var problems))
            {
                return SaveOutcome.Invalid(previous.Version, problems);
            }

            // 3. Connectivity, unless the user explicitly chose to save anyway.
            IReadOnlyList<AccountProbeResult> probes = [];
            if (!force)
            {
                probes = await probe.ProbeAll(candidate.Accounts, ct);
                if (probes.Any(p => !p.Ok))
                {
                    return SaveOutcome.Unreachable(previous.Version, probes);
                }
            }

            // 4. Prove the graph builds before committing anything.
            IImmichFrameLogic candidateLogic;
            try
            {
                candidateLogic = logicFactory.Build(candidate);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Candidate settings could not be turned into an account graph.");
                return SaveOutcome.Invalid(previous.Version, [$"Could not build the account graph: {ex.Message}"]);
            }

            // 5. Persist. If this throws, memory and disk are both untouched.
            try
            {
                writer.Write(candidate);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write settings to {Path}", writer.TargetPath);
                return SaveOutcome.WriteFailed(previous.Version,
                    $"Could not write {writer.TargetPath}: {ex.Message}");
            }

            // ---- past this point nothing throws ----

            var next = new SettingsGeneration
            {
                Version = previous.Version + 1,
                Settings = candidate,
                Logic = candidateLogic,
                CreatedAt = DateTimeOffset.UtcNow
            };

            // 6. One exchange publishes the settings and the graph together.
            store.Swap(next);

            // 7. Drop derived caches that would otherwise mask the change for minutes.
            foreach (var resettable in resettables)
            {
                try
                {
                    resettable.ResetCaches();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to reset caches on {Service}", resettable.GetType().Name);
                }
            }

            // 8. Release the old accounts once nothing can still be routed to them.
            ScheduleRetirement(previous);

            logger.LogInformation("Settings generation {Version} published ({AccountCount} account(s)).",
                next.Version, candidate.Accounts.Count());

            return new SaveOutcome(SaveStatus.Applied, next.Version, [], probes, CollectWarnings());
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public async Task<IReadOnlyList<AccountProbeResult>> ValidateOnlyAsync(
        ServerSettings candidate, CancellationToken ct = default) =>
        await probe.ProbeAll(candidate.Accounts, ct);

    /// <summary>Conditions that change how the config will be read on the next start.</summary>
    public List<string> CollectWarnings()
    {
        var warnings = new List<string>();

        foreach (var shadowed in location.ShadowedFiles())
        {
            warnings.Add($"'{Path.GetFileName(shadowed)}' is now ignored: Settings.json takes precedence when both exist.");
        }

        return warnings;
    }

    private void ScheduleRetirement(SettingsGeneration previous)
    {
        if (previous.Logic is not MultiImmichFrameLogicDelegate old) return;

        var retired = old.AccountLogics.ToList();
        if (retired.Count == 0) return;

        _ = Task.Delay(RetirementGrace).ContinueWith(_ =>
        {
            try
            {
                tracker.Forget(retired);
                logger.LogDebug("Released {Count} retired account(s) from the asset tracker.", retired.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to release retired accounts from the asset tracker.");
            }
        });
    }
}
