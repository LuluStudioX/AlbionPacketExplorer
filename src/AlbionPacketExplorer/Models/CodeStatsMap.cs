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
    /// Adopts another map whose codes are DISJOINT from this one (the shard router guarantees a
    /// (kind, code) only ever lands on one shard), so adoption is a plain union - no stat merging.
    /// </summary>
    public void AdoptDisjoint(CodeStatsMap other)
    {
        foreach (var (key, stats) in other.Map)
            Map[key] = stats;
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
            // KeyStats.CountKeyFor; display formatting happens at render in TopValuesDisplay.
            object repr = KeyStats.CountKeyFor(paramVal);
            if (ks.ValueCounts.TryGetValue(repr, out var c))
                ks.ValueCounts[repr] = c + 1;
            else if (ks.ValueCounts.Count < KeyStats.DistinctCap)
                ks.ValueCounts[repr] = 1;
            else
                ks.DistinctCapped = true;

            if (ToDouble(paramVal.Value) is { } d)
            {
                ks.NumericMin = ks.NumericMin is { } mn ? Math.Min(mn, d) : d;
                ks.NumericMax = ks.NumericMax is { } mx ? Math.Max(mx, d) : d;
            }
        }
    }

    public static double? ToDouble(object? v) => v switch
    {
        byte b   => b,
        short s  => s,
        int i    => i,
        long l   => l,
        float f  => f,
        double d => d,
        _        => null
    };
}
