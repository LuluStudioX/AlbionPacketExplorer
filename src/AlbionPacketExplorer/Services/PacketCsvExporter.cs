using System.Globalization;
using System.Text;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Exports decoded packets to CSV in long form: one row per param field
/// (timestamp, kind, code, name, key, type, value). Packets with no params emit one row with empty
/// field columns. Spreadsheet-friendly for ad-hoc analysis.
/// </summary>
public static class PacketCsvExporter
{
    public static async Task WriteAsync(Stream stream, IEnumerable<PacketEntry> packets)
    {
        await using var w = new StreamWriter(stream, new UTF8Encoding(false));
        await w.WriteLineAsync("timestamp,kind,code,name,key,type,value");

        foreach (var p in packets)
        {
            var ts = p.Timestamp.ToString("o", CultureInfo.InvariantCulture);
            var name = PacketNameResolver.Resolve(p.Kind, p.Code);
            var code = p.Code.ToString(CultureInfo.InvariantCulture);

            if (p.Params.Count == 0)
            {
                await w.WriteLineAsync(Row(ts, p.Kind, code, name, "", "", ""));
                continue;
            }

            foreach (var (key, pv) in p.Params.OrderBy(kv => int.TryParse(kv.Key, out var n) ? n : int.MaxValue))
            {
                var value = PacketDisplayFormatter.FormatParamValue(pv);
                await w.WriteLineAsync(Row(ts, p.Kind, code, name, key, pv.Type, value));
            }
        }
    }

    private static string Row(params string[] fields) => string.Join(",", fields.Select(Csv));

    private static string Csv(string s)
    {
        if (s.IndexOfAny([',', '"', '\n', '\r']) < 0) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
