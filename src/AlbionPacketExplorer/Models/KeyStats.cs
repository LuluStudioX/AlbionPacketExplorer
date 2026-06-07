namespace AlbionPacketExplorer.Models;

public class KeyStats
{
    public string Key { get; set; } = "";
    public int PresenceCount { get; set; }
    public int TotalPackets { get; set; }
    public double PresencePct => TotalPackets == 0 ? 0 : (double) PresenceCount / TotalPackets * 100;
    public HashSet<string> Types { get; } = [];
    public List<object?> SampleValues { get; } = [];

    // ── Value distribution (populated by the aggregator) ──────────────────────────────
    // Stringified value -> times seen. Capped so a high-cardinality field (ids, ticks) cannot
    // grow this without bound; once the cap is hit only already-seen values keep counting.
    public const int DistinctCap = 500;
    public Dictionary<string, int> ValueCounts { get; } = [];
    public bool DistinctCapped { get; set; }

    // Numeric range across observed numeric values (null when the field is never numeric).
    public double? NumericMin { get; set; }
    public double? NumericMax { get; set; }
    public bool HasNumericRange => NumericMin.HasValue && NumericMax.HasValue;

    /// <summary>Distinct values seen, shown as "500+" once the cap was exceeded.</summary>
    public string DistinctDisplay => DistinctCapped ? $"{DistinctCap}+" : ValueCounts.Count.ToString();

    /// <summary>True when the field only ever held one value (a constant / likely default).</summary>
    public bool IsConstant => !DistinctCapped && ValueCounts.Count == 1;

    public string TypesDisplay => string.Join(", ", Types);

    public string MinMaxDisplay
    {
        get
        {
            if (!HasNumericRange) return "";
            var min = NumericMin!.Value;
            var max = NumericMax!.Value;
            return min == max ? Fmt(min) : $"{Fmt(min)} .. {Fmt(max)}";
        }
    }

    /// <summary>Most common values, "value xN", highest first.</summary>
    public string TopValuesDisplay
    {
        get
        {
            if (ValueCounts.Count == 0) return "";
            var top = ValueCounts
                .OrderByDescending(kv => kv.Value)
                .Take(5)
                .Select(kv => kv.Value > 1 ? $"{Trim(kv.Key)} x{kv.Value}" : Trim(kv.Key));
            return string.Join("   ", top);
        }
    }

    private static string Fmt(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long) d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Trim(string s) => s.Length <= 40 ? s : s[..37] + "...";
}
