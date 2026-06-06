using AlbionPacketExplorer.Models;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public class PacketFileReader
{
    public async IAsyncEnumerable<PacketEntry> ReadAsync(string filePath, IProgress<double>? progress = null)
    {
        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        bool isArray = await IsJsonArrayAsync(stream);
        stream.Position = 0;

        if (isArray)
        {
            await foreach (var entry in ReadArrayAsync(stream, totalBytes, progress))
                yield return entry;
        }
        else
        {
            await foreach (var entry in ReadNdjsonAsync(stream, totalBytes, progress))
                yield return entry;
        }
    }

    private static async Task<bool> IsJsonArrayAsync(Stream stream)
    {
        var buf = new byte[64];
        int read = await stream.ReadAsync(buf);
        for (int i = 0; i < read; i++)
        {
            if (buf[i] == '[') return true;
            if (buf[i] == '{') return false;
        }
        return false;
    }

    private static async IAsyncEnumerable<PacketEntry> ReadNdjsonAsync(Stream stream, long totalBytes, IProgress<double>? progress)
    {
        long bytesRead = 0;
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            bytesRead += line.Length + 1;
            progress?.Report((double)bytesRead / totalBytes);

            if (string.IsNullOrWhiteSpace(line)) continue;

            PacketEntry? entry = null;
            try { entry = ParseLine(line); } catch { }
            if (entry != null) yield return entry;
        }
    }

    private static async IAsyncEnumerable<PacketEntry> ReadArrayAsync(Stream stream, long totalBytes, IProgress<double>? progress)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var entries = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream, options);
        if (entries == null) yield break;

        progress?.Report(1.0);

        foreach (var el in entries)
        {
            PacketEntry? entry = null;
            try { entry = ParseElement(el); } catch { }
            if (entry != null) yield return entry;
        }
    }

    private static PacketEntry? ParseLine(string line)
    {
        using var doc = JsonDocument.Parse(line);
        return ParseElement(doc.RootElement);
    }

    private static PacketEntry? ParseElement(JsonElement root)
    {
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

        // Photon response framing (present only on RESPONSE packets we captured ourselves).
        int? returnCode = root.TryGetProperty("returnCode", out var rcEl) && rcEl.ValueKind == JsonValueKind.Number
            ? rcEl.GetInt32() : null;
        string? debugMessage = root.TryGetProperty("debugMessage", out var dmEl) && dmEl.ValueKind == JsonValueKind.String
            ? dmEl.GetString() : null;

        return new PacketEntry(ts, kind, code, parameters, returnCode, debugMessage);
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
