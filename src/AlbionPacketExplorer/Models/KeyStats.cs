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
    // Value -> times seen. Scalar values (numbers, bools, strings) are keyed by their raw boxed
    // value so the load path never formats them; arrays/dicts are keyed by their formatted string
    // so identical contents still dedupe (raw references never would). Formatting of the retained
    // entries happens only at render time in TopValuesDisplay. Capped so a high-cardinality field
    // (ids, ticks) cannot grow this without bound; once the cap is hit only already-seen values
    // keep counting.
    public const int DistinctCap = 500;
    public Dictionary<object, int> ValueCounts { get; } = [];
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
                .Select(kv => kv.Value > 1 ? $"{Trim(FormatKey(kv.Key))} x{kv.Value}" : Trim(FormatKey(kv.Key)));
            return string.Join("   ", top);
        }
    }

    // Renders a counted key: strings are already display text (raw string values, array reprs and
    // the "(null)" marker); raw longs get the same ticks-aware formatting the params view uses;
    // other boxed scalars print as before (ToString matched the old formatter's output for them).
    private static string FormatKey(object key) => key switch
    {
        string s => s,
        long l => Services.PacketDisplayFormatter.FormatInt64(l),
        _ => key.ToString() ?? "(null)"
    };

    /// <summary>
    /// The single rule for what keys <see cref="ValueCounts"/>: scalars count by their raw boxed
    /// value (the box already exists in the ParamSet; boxed primitives hash/compare by value, so
    /// the load hot path formats nothing), arrays/dicts by their formatted string so identical
    /// contents still dedupe, nulls by the "(null)" marker (dictionary keys cannot be null).
    /// Shared by the aggregator's per-packet pass and the parallel per-batch partials so both
    /// produce identical buckets.
    /// </summary>
    public static object CountKeyFor(ParamValue pv) => pv.Value switch
    {
        null => "(null)",
        long or int or short or byte or sbyte or ushort or uint
            or double or float or bool or string => pv.Value,
        _ => Services.PacketDisplayFormatter.FormatParamValue(pv)
    };

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
