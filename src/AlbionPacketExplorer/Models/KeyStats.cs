namespace AlbionPacketExplorer.Models;

/// <summary>
/// A value-type key for <see cref="KeyStats.ValueCounts"/>. Replaces the old <c>object</c> key so the
/// load hot path never hashes/compares a boxed primitive (16M+ lookups per big load): scalars carry
/// their bits inline (numbers in <c>_num</c>, doubles as their bit pattern, bools as 0/1), only
/// strings and array/dict reprs hold a reference. Display formatting happens once, at render.
/// </summary>
public readonly struct StatKey : IEquatable<StatKey>
{
    private enum Tag : byte { Null, Long, Double, Bool, Text }

    private readonly Tag _tag;
    private readonly long _num;       // Long value | Double bit pattern | Bool (0/1)
    private readonly string? _text;   // Text only

    private StatKey(Tag tag, long num, string? text)
    {
        _tag = tag;
        _num = num;
        _text = text;
    }

    public static readonly StatKey Null = new(Tag.Null, 0, null);
    public static StatKey FromLong(long v) => new(Tag.Long, v, null);
    public static StatKey FromDouble(double v) => new(Tag.Double, BitConverter.DoubleToInt64Bits(v), null);
    public static StatKey FromBool(bool v) => new(Tag.Bool, v ? 1 : 0, null);
    public static StatKey FromText(string v) => new(Tag.Text, 0, v);

    /// <summary>The numeric value for min/max tracking, or null when the key is non-numeric.</summary>
    public double? Numeric => _tag switch
    {
        Tag.Long => _num,
        Tag.Double => BitConverter.Int64BitsToDouble(_num),
        _ => null
    };

    public bool Equals(StatKey other) =>
        _tag == other._tag && _num == other._num && string.Equals(_text, other._text, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is StatKey k && Equals(k);

    public override int GetHashCode() =>
        _tag == Tag.Text ? HashCode.Combine(_tag, _text) : HashCode.Combine(_tag, _num);

    /// <summary>Render this key to display text (matches the old per-type formatting).</summary>
    public string Display() => _tag switch
    {
        Tag.Null => "(null)",
        Tag.Long => Services.PacketDisplayFormatter.FormatInt64(_num),
        Tag.Double => FmtDouble(BitConverter.Int64BitsToDouble(_num)),
        Tag.Bool => _num != 0 ? "True" : "False",
        Tag.Text => _text ?? "(null)",
        _ => "(null)"
    };

    private static string FmtDouble(double d) =>
        d == Math.Floor(d) && Math.Abs(d) < 1e15
            ? ((long) d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

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
    public Dictionary<StatKey, int> ValueCounts { get; } = [];
    public bool DistinctCapped { get; set; }

    // Numeric range across observed numeric values (null when the field is never numeric).
    public double? NumericMin { get; set; }
    public double? NumericMax { get; set; }
    public bool HasNumericRange => NumericMin.HasValue && NumericMax.HasValue;

    /// <summary>Distinct values seen, shown as "500+" once the cap was exceeded.</summary>
    public string DistinctDisplay => DistinctCapped ? $"{DistinctCap}+" : ValueCounts.Count.ToString();

    /// <summary>True when the field only ever held one value (a constant / likely default).</summary>
    public bool IsConstant => !DistinctCapped && ValueCounts.Count == 1;

    /// <summary>
    /// Folds another partition's stats for the SAME key into this one. Presence, types, value counts
    /// and numeric range merge exactly; sample values fill up to 5 in arrival order; the distinct cap
    /// is re-applied as counts combine (so the capped flag and which values are retained can differ
    /// slightly from a single-threaded run, which only affects the display hints).
    /// </summary>
    public void MergeFrom(KeyStats other)
    {
        PresenceCount += other.PresenceCount;

        foreach (var t in other.Types)
            Types.Add(t);

        foreach (var v in other.SampleValues)
        {
            if (SampleValues.Count >= 5) break;
            SampleValues.Add(v);
        }

        foreach (var (k, c) in other.ValueCounts)
        {
            if (ValueCounts.TryGetValue(k, out var existing))
                ValueCounts[k] = existing + c;
            else if (ValueCounts.Count < DistinctCap)
                ValueCounts[k] = c;
            else
                DistinctCapped = true;
        }
        if (other.DistinctCapped) DistinctCapped = true;

        if (other.NumericMin is { } omn)
            NumericMin = NumericMin is { } mn ? Math.Min(mn, omn) : omn;
        if (other.NumericMax is { } omx)
            NumericMax = NumericMax is { } mx ? Math.Max(mx, omx) : omx;
    }

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
                .Select(kv => kv.Value > 1 ? $"{Trim(kv.Key.Display())} x{kv.Value}" : Trim(kv.Key.Display()));
            return string.Join("   ", top);
        }
    }

    /// <summary>
    /// The single rule for what keys <see cref="ValueCounts"/>: scalars carry their bits inline in a
    /// <see cref="StatKey"/> (no boxed hashing on the load hot path), arrays/dicts key by their
    /// formatted string so identical contents still dedupe, nulls by the "(null)" marker. Shared by
    /// the aggregator's per-packet pass and the parallel partials so both produce identical buckets.
    /// </summary>
    public static StatKey CountKeyFor(ParamValue pv) => pv.ToStatKey();

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
