namespace AlbionPacketExplorer.Models;

/// <summary>
/// The packet-statistics accumulator: (kind, code) -> <see cref="CodeStats"/> with the per-key
/// field distributions. THE single ingest implementation - the aggregator uses one instance for
/// per-packet ingest (live capture, raw replay), and the sharded file-load path runs one instance
/// per shard over a disjoint subset of codes, so both paths execute identical code per packet.
/// Not thread-safe; each instance is owned by exactly one thread at a time.
/// </summary>
public sealed class CodeStatsMap
{
    public Dictionary<(string Kind, int Code), CodeStats> Map { get; } = [];

    public void Ingest(string kind, int code, ParamSet ps)
    {
        var key = (kind, code);
        if (!Map.TryGetValue(key, out var stats))
            Map[key] = stats = new CodeStats { Kind = kind, Code = code };

        stats.Count++;
        UpdateKeyStats(stats, ps);
    }

    /// <summary>
    /// Merges another map into this one. The arrival-partitioned workers each see a slice of every
    /// (kind, code), so their maps overlap; merging accumulates counts and key distributions. A
    /// (kind, code) or key absent here is adopted wholesale (cheap, no copy); when both have it, the
    /// per-key <see cref="KeyStats.MergeFrom"/> folds the other side in. Merge the workers in worker
    /// order (0..N) so the kept sample values come from the earliest batches first.
    /// </summary>
    public void Merge(CodeStatsMap other)
    {
        foreach (var (key, os) in other.Map)
        {
            if (!Map.TryGetValue(key, out var ts))
            {
                Map[key] = os;
                continue;
            }

            ts.Count += os.Count;
            foreach (var (paramKey, oks) in os.Keys)
            {
                if (ts.Keys.TryGetValue(paramKey, out var tks))
                    tks.MergeFrom(oks);
                else
                    ts.Keys[paramKey] = oks;
            }
        }
    }

    private static void UpdateKeyStats(CodeStats stats, ParamSet ps)
    {
        foreach (var (paramKey, paramVal) in ps)
        {
            if (paramKey == "252" || paramKey == "253") continue;

            if (!stats.Keys.TryGetValue(paramKey, out var ks))
                stats.Keys[paramKey] = ks = new KeyStats { Key = paramKey };

            ks.PresenceCount++;
            ks.Types.Add(paramVal.Type);
            if (ks.SampleValues.Count < 5)
                ks.SampleValues.Add(paramVal.Value);

            // Value distribution: count distinct values (capped) and track the numeric range so a
            // field's shape (constant / enum-like / id / range) is visible. The key rule lives in
            // KeyStats.CountKeyFor; display formatting happens at render in TopValuesDisplay. The
            // StatKey carries the numeric value inline, so min/max needs no second unbox.
            StatKey repr = KeyStats.CountKeyFor(paramVal);
            if (ks.ValueCounts.TryGetValue(repr, out var c))
                ks.ValueCounts[repr] = c + 1;
            else if (ks.ValueCounts.Count < KeyStats.DistinctCap)
                ks.ValueCounts[repr] = 1;
            else
                ks.DistinctCapped = true;

            if (repr.Numeric is { } d)
            {
                ks.NumericMin = ks.NumericMin is { } mn ? Math.Min(mn, d) : d;
                ks.NumericMax = ks.NumericMax is { } mx ? Math.Max(mx, d) : d;
            }
        }
    }
}
