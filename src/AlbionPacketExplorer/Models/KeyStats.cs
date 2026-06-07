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

    /// <summary>
    /// Best-effort guess at what the field represents, from its observed types and value spread.
    /// Hints only (the curated schema is authoritative); meant to speed annotation.
    /// </summary>
    public string Heuristic
    {
        get
        {
            if (Types.Contains("Byte[]")) return "bytes / GUID";
            if (Types.Any(t => t.EndsWith("[]"))) return "array";
            if (Types.Contains("String")) return "string / uniqueName";
            if (Types.Contains("Boolean")) return "bool";
            if (IsConstant) return "constant";

            if (HasNumericRange)
            {
                var min = NumericMin!.Value;
                var max = NumericMax!.Value;
                if (min > 5e17 && max < 8e17) return "ticks (UTC)";       // .NET DateTime ticks, modern dates
                if (min > 1e12 && max < 3e12) return "unix ms?";          // epoch milliseconds
                var distinct = DistinctCapped ? int.MaxValue : ValueCounts.Count;
                if (min >= 0 && max <= 255 && distinct <= 16) return "enum / flag";
                if (min >= 0 && max > 1000 && (DistinctCapped || distinct > 50)) return "id";
            }
            return "";
        }
    }

    private static string Fmt(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long) d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Trim(string s) => s.Length <= 40 ? s : s[..37] + "...";
}
