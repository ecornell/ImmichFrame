using ImmichFrame.Core.Interfaces;
using ImmichFrame.WebApi.Models;

namespace ImmichFrame.WebApi.Settings;

/// <summary>
/// An immutable snapshot pairing a settings object with the account graph built from it.
/// <para>
/// Settings and the graph are published together so there is never a window in which new settings
/// are live against a stale <see cref="IImmichFrameLogic"/> (or the reverse).
/// </para>
/// </summary>
public sealed class SettingsGeneration
{
    /// <summary>Monotonic token used for optimistic concurrency on the admin API.</summary>
    public required long Version { get; init; }

    public required ServerSettings Settings { get; init; }

    public required IImmichFrameLogic Logic { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
