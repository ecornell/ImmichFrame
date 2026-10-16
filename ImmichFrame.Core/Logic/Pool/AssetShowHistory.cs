using System.Text.Json;
using ImmichFrame.Core.Api;
using ImmichFrame.Core.Interfaces;

namespace ImmichFrame.Core.Logic.Pool;

/// <summary>
/// Shared by every pool and client and kept outside the pools, so neither a settings reload nor a
/// restart (with <see cref="Load"/>/<see cref="Save"/>) makes recently shown assets eligible again.
/// </summary>
/// <remarks>
/// Choosing at random from the oldest quarter, rather than the single oldest asset, keeps the order
/// from repeating each round while guaranteeing about three quarters of the pool between repeats.
/// </remarks>
public sealed class AssetShowHistory(TimeProvider? timeProvider = null) : IAssetShowHistory
{
    internal const double WindowFraction = 0.25;
    internal const int MaxEntries = 100_000;

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, long> _lastShown = [];
    private long _lastStamp;
    private bool _dirty;

    public IReadOnlyList<AssetResponseDto> PickLeastRecent(IEnumerable<AssetResponseDto> candidates, int requested)
    {
        if (requested <= 0) return [];
        var assets = candidates.DistinctBy(asset => asset.Id).ToArray();
        if (assets.Length == 0) return [];

        // Shuffling first makes the stable sort break ties (e.g. never shown) at random.
        Random.Shared.Shuffle(assets);
        AssetResponseDto[] ordered;
        lock (_gate)
        {
            ordered = assets.OrderBy(asset => _lastShown.GetValueOrDefault(asset.Id, long.MinValue)).ToArray();
        }

        var window = Math.Min(ordered.Length, Math.Max(requested, (int)Math.Ceiling(ordered.Length * WindowFraction)));
        var eligible = ordered[..window];
        Random.Shared.Shuffle(eligible);
        return eligible.Take(requested).ToArray();
    }

    public void RecordShown(Guid assetId)
    {
        lock (_gate)
        {
            // Strictly increasing, so assets shown within the same millisecond still have an order.
            _lastStamp = Math.Max(_lastStamp + 1, _time.GetUtcNow().ToUnixTimeMilliseconds());
            _lastShown[assetId] = _lastStamp;
            _dirty = true;
            if (_lastShown.Count > MaxEntries) TrimOldest();
        }
    }

    internal long? LastShown(Guid assetId)
    {
        lock (_gate) return _lastShown.TryGetValue(assetId, out var stamp) ? stamp : null;
    }

    private void TrimOldest()
    {
        foreach (var id in _lastShown.OrderBy(entry => entry.Value).Take(MaxEntries / 10).Select(entry => entry.Key).ToArray())
            _lastShown.Remove(id);
    }

    /// <summary>Merges a saved history, keeping the newer stamp for an asset recorded since startup.</summary>
    public void Load(string path)
    {
        if (!File.Exists(path)) return;
        var saved = JsonSerializer.Deserialize<SavedHistory>(File.ReadAllText(path));
        if (saved?.LastShown == null) return;

        lock (_gate)
        {
            foreach (var (id, stamp) in saved.LastShown)
            {
                if (!_lastShown.TryGetValue(id, out var current) || current < stamp)
                    _lastShown[id] = stamp;
                _lastStamp = Math.Max(_lastStamp, stamp);
            }
            if (_lastShown.Count > MaxEntries) TrimOldest();
        }
    }

    /// <summary>Writes the history if it changed since the last save. Returns whether it wrote.</summary>
    public bool Save(string path)
    {
        SavedHistory snapshot;
        lock (_gate)
        {
            if (!_dirty) return false;
            snapshot = new SavedHistory { LastShown = new Dictionary<Guid, long>(_lastShown) };
            _dirty = false;
        }

        try
        {
            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot));
                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }
        catch
        {
            lock (_gate) _dirty = true;
            throw;
        }

        return true;
    }

    private sealed class SavedHistory
    {
        public int Version { get; set; } = 1;
        public Dictionary<Guid, long>? LastShown { get; set; }
    }
}
