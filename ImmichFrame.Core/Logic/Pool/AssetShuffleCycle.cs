using ImmichFrame.Core.Api;

namespace ImmichFrame.Core.Logic.Pool;

// Owned by one account/source pool. History tracks selection, not client playback.
internal sealed class AssetShuffleCycle
{
    private readonly object _gate = new();
    private readonly HashSet<Guid> _seen = [];
    private Guid? _last;

    public AssetResponseDto[] Take(IEnumerable<AssetResponseDto> source, int requested)
    {
        if (requested <= 0) return [];
        lock (_gate)
        {
            var assets = source.DistinctBy(asset => asset.Id).ToArray();
            var ids = assets.Select(asset => asset.Id).ToHashSet();
            // Cache refreshes must not restart an unchanged album's cycle.
            _seen.IntersectWith(ids);
            var remaining = assets.Where(asset => !_seen.Contains(asset.Id)).ToArray();
            if (remaining.Length == 0)
            {
                _seen.Clear();
                remaining = assets;
            }

            Random.Shared.Shuffle(remaining);
            if (remaining.Length > 1 && remaining[0].Id == _last)
                (remaining[0], remaining[1]) = (remaining[1], remaining[0]);

            // Return a short batch at the cycle boundary rather than repeat in a batch.
            var result = remaining.Take(requested).ToArray();
            foreach (var asset in result) _seen.Add(asset.Id);
            if (result.Length > 0) _last = result[^1].Id;
            return result;
        }
    }
}
