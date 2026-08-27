namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// Holds the currently published <see cref="SettingsGeneration"/>.
/// <para>
/// Reads are lock-free: a single volatile read of a reference field. A request that has already read
/// <see cref="Current"/> keeps running against that generation even if a swap happens mid-flight,
/// so an in-progress operation always sees a coherent settings+graph pair.
/// </para>
/// </summary>
public sealed class SettingsStore
{
    private SettingsGeneration? _current;

    /// <summary>The published generation. Throws if accessed before bootstrap.</summary>
    public SettingsGeneration Current =>
        Volatile.Read(ref _current)
        ?? throw new InvalidOperationException(
            "Settings have not been initialized yet. SettingsBootstrapper.Initialize() must run before first use.");

    public bool IsInitialized => Volatile.Read(ref _current) is not null;

    internal void Initialize(SettingsGeneration generation) => Volatile.Write(ref _current, generation);

    /// <summary>Atomically publishes <paramref name="next"/> and returns the generation it replaced.</summary>
    internal SettingsGeneration? Swap(SettingsGeneration next) => Interlocked.Exchange(ref _current, next);
}
