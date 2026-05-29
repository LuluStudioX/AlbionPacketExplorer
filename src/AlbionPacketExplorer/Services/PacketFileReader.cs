using AlbionPacketExplorer.Models;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public class PacketFileReader
{
    public async IAsyncEnumerable<PacketEntry> ReadAsync(string filePath, IProgress<double>? progress = null)
    {
        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;
        long bytesRead = 0;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            bytesRead += line.Length + 1;
            progress?.Report((double) bytesRead / totalBytes);

            if (string.IsNullOrWhiteSpace(line)) continue;

            PacketEntry? entry = null;
            try { entry = ParseLine(line); } catch { }
            if (entry != null) yield return entry;
        }
    }

    private static PacketEntry? ParseLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ts", out var tsEl) ||
            !root.TryGetProperty("kind", out var kindEl) ||
            !root.TryGetProperty("code", out var codeEl) ||
            !root.TryGetProperty("params", out var paramsEl))
            return null;

        var ts = tsEl.GetDateTime();
        var kind = kindEl.GetString() ?? "";
        var code = codeEl.GetInt32();
        var parameters = new Dictionary<string, ParamValue>();

        foreach (var param in paramsEl.EnumerateObject())
        {
            var type = param.Value.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
            object? value = param.Value.TryGetProperty("value", out var valueEl) ? ExtractValue(valueEl) : null;
            parameters[param.Name] = new ParamValue(type, value);
        }

        return new PacketEntry(ts, kind, code, parameters);
    }

    private static object? ExtractValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number when el.TryGetInt64(out var l) => l,
        JsonValueKind.Number when el.TryGetDouble(out var d) => d,
        JsonValueKind.String => el.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => el.EnumerateArray().Select(ExtractValue).ToList(),
        JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => ExtractValue(p.Value)),
        _ => el.ToString()
    };
}
