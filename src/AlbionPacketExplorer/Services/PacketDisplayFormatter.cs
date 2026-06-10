using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

public static class PacketDisplayFormatter
{
    private static readonly long AlbionTicksThreshold = 637_000_000_000_000_000L;

    public static string FormatParamSummary(PacketEntry packet) => FormatParamSummary(packet.Params);

    /// <summary>
    /// Overload that formats an already-decoded <see cref="ParamSet"/>. Callers that also need the
    /// raw params for another pass (e.g. the filter's on-the-fly name resolution) decode once and
    /// pass the set here so the memory-mapped store is not hit twice for the same packet.
    /// </summary>
    public static string FormatParamSummary(ParamSet @params)
    {
        var parts = @params
            .Where(p => p.Key != "252" && p.Key != "253")
            .OrderBy(p => int.TryParse(p.Key, out var n) ? n : 999)
            .Select(p => $"{p.Key}={FormatValueShort(p.Value.Value)}");
        return string.Join("  ", parts);
    }

    public static string FormatKeySummary(CodeStats stats)
    {
        var parts = stats.Keys
            .OrderBy(k => int.TryParse(k.Key, out var n) ? n : 999)
            .Select(k => $"{k.Key}({k.Value.PresencePct:F0}%)");
        return string.Join(", ", parts);
    }

    public static string FormatParamValue(ParamValue pv) => FormatValue(pv.Type, pv.Value);

    private static string FormatValue(string type, object? value)
    {
        if (value == null) return "(null)";

        if (type == "Int64" && value is long l)
            return FormatInt64(l);

        if (type == "Byte[]" && value is List<object?> byteList)
            return FormatByteArray(byteList);

        if (value is List<object?> list)
            return FormatList(list);

        if (value is System.Collections.IList arr)
            return FormatList(arr.Cast<object?>().ToList());

        if (value is System.Collections.IDictionary dict)
        {
            var parts = new List<string>(dict.Count);
            foreach (System.Collections.DictionaryEntry e in dict)
                parts.Add($"{e.Key}:{e.Value}");
            return $"{{{string.Join(", ", parts)}}}";
        }

        return value.ToString() ?? "(null)";
    }

    /// <summary>
    /// Formats an Int64 the way the params view shows it (ticks-looking values get the UTC date
    /// suffix). Public so KeyStats can format raw counted values at render time.
    /// </summary>
    public static string FormatInt64(long l)
    {
        // Albion timestamps are .NET ticks; values above threshold are dates
        if (l > AlbionTicksThreshold)
        {
            try
            {
                var dt = new DateTime(l, DateTimeKind.Utc);
                return $"{l} ({dt:yyyy-MM-dd HH:mm:ss} UTC)";
            }
            catch (ArgumentOutOfRangeException) { }
        }
        return l.ToString();
    }

    // Byte-array elements arrive as long (file-loaded numbers), as narrower numeric types (live
    // capture: byte/short/int), or as numeric strings (older captures that stored bytes as quoted
    // strings). Accept all so hex + GUID rendering works regardless of source encoding.
    private static bool TryToByte(object? o, out byte result)
    {
        switch (o)
        {
            case long l:  result = unchecked((byte)l); return true;
            case int i:   result = unchecked((byte)i); return true;
            case short s: result = unchecked((byte)s); return true;
            case byte b:  result = b; return true;
            case string str when long.TryParse(str, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var ls):
                result = unchecked((byte)ls); return true;
            default: result = 0; return false;
        }
    }

    private static string FormatByteArray(List<object?> bytes)
    {
        var hex = string.Join(" ", bytes.Select(b => TryToByte(b, out var bv) ? bv.ToString("X2") : "??"));
        var dec = string.Join(", ", bytes.Select(b => b?.ToString() ?? "?"));

        if (bytes.Count == 16)
        {
            var raw = new byte[16];
            var allBytes = true;
            for (int i = 0; i < 16; i++)
                if (!TryToByte(bytes[i], out raw[i])) { allBytes = false; break; }
            if (allBytes)
            {
                try { return $"{new Guid(raw)} [{hex}]"; }
                catch { }
            }
        }

        return $"[{hex}] ({dec})";
    }

    private static string FormatList(List<object?> list)
    {
        var items = list.Select(v => v?.ToString() ?? "null");
        return $"[{string.Join(", ", items)}]";
    }

    private static string FormatValueShort(object? v) => v switch
    {
        null => "null",
        System.Collections.IList list => $"[{list.Count}]",
        _ => v.ToString() ?? "null"
    };
}
