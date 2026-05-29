using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

public static class PacketDisplayFormatter
{
    private static readonly long AlbionTicksThreshold = 637_000_000_000_000_000L;

    public static string FormatParamSummary(PacketEntry packet)
    {
        var parts = packet.Params
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

        return value.ToString() ?? "(null)";
    }

    private static string FormatInt64(long l)
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

    private static string FormatByteArray(List<object?> bytes)
    {
        var hex = string.Join(" ", bytes.Select(b => b is long lb ? lb.ToString("X2") : "??"));
        var dec = string.Join(", ", bytes.Select(b => b?.ToString() ?? "?"));
        return $"[{hex}] ({dec})";
    }

    private static string FormatList(List<object?> list)
    {
        const int maxItems = 20;
        var items = list.Take(maxItems).Select(v => v?.ToString() ?? "null");
        var overflow = list.Count > maxItems ? $" …{list.Count - maxItems} more" : "";
        return $"[{string.Join(", ", items)}{overflow}]";
    }

    private static string FormatValueShort(object? v) => v switch
    {
        null => "null",
        System.Collections.IList list => $"[{list.Count}]",
        _ => v.ToString() ?? "null"
    };
}
