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

        if (value is System.Collections.IList arr)
            return FormatList(arr.Cast<object?>().ToList());

        if (value is System.Collections.IDictionary dict)
        {
            var parts = dict.Keys.Cast<object?>().Select(k => $"{k}:{dict[k]}");
            return $"{{{string.Join(", ", parts)}}}";
        }

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

        if (bytes.Count == 16)
        {
            try
            {
                var raw = bytes.Select(b => b is long lb ? (byte)lb : (byte)0).ToArray();
                var guid = new Guid(raw);
                return $"{guid} [{hex}]";
            }
            catch { }
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
