using AlbionPacketExplorer.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public class PacketFileReader
{
    // Report progress at most this often so a multi-million line load does not flood the
    // dispatcher with callbacks; a final Report(1.0) always fires when the stream ends.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(75);

    public async IAsyncEnumerable<PacketEntry> ReadAsync(string filePath, IProgress<double>? progress = null)
    {
        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        bool isArray = await IsJsonArrayAsync(stream);
        stream.Position = 0;

        if (isArray)
        {
            await foreach (var entry in ReadArrayAsync(stream, progress))
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
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            bytesRead += line.Length + 1;
            if (progress != null && totalBytes > 0)
            {
                var now = sw.Elapsed;
                if (now - lastReport >= ProgressInterval)
                {
                    progress.Report((double)bytesRead / totalBytes);
                    lastReport = now;
                }
            }

            if (string.IsNullOrWhiteSpace(line)) continue;

            PacketEntry? entry = null;
            try { entry = ParseLine(line); } catch { }
            if (entry != null) yield return entry;
        }

        progress?.Report(1.0);
    }

    private static async IAsyncEnumerable<PacketEntry> ReadArrayAsync(Stream stream, IProgress<double>? progress)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Stream the array element-by-element so a giant array file is never fully materialized.
        await foreach (var el in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options))
        {
            PacketEntry? entry = null;
            try { entry = ParseElement(el); } catch { }
            if (entry != null) yield return entry;
        }

        progress?.Report(1.0);
    }

    // NDJSON parse: tokenize one line's UTF-8 bytes with Utf8JsonReader instead of building a
    // JsonDocument DOM per line. Produces a PacketEntry byte-for-byte equivalent to ParseElement.
    private static PacketEntry? ParseLine(string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line);
        var reader = new Utf8JsonReader(bytes);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        DateTime? ts = null;
        bool sawKind = false;
        string kind = "";
        int? code = null;
        ParamSet? parameters = null;
        bool sawParams = false;
        int? returnCode = null;
        string? debugMessage = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string propName = reader.GetString()!;
            reader.Read(); // advance to the value

            switch (propName)
            {
                case "ts":
                    ts = reader.GetDateTime();
                    break;
                case "kind":
                    // Matches ParseElement: GetString() ?? "" (JSON null -> empty string).
                    // Intern: the 3 Kind values (EVENT/REQUEST/RESPONSE) dedupe to single instances.
                    kind = string.Intern(reader.GetString() ?? "");
                    sawKind = true;
                    break;
                case "code":
                    code = reader.GetInt32();
                    break;
                case "params":
                    parameters = ReadParams(ref reader);
                    sawParams = true;
                    break;
                case "returnCode":
                    // Photon response framing: number only (mirrors JsonValueKind.Number guard).
                    if (reader.TokenType == JsonTokenType.Number)
                        returnCode = reader.GetInt32();
                    else
                        reader.Skip();
                    break;
                case "debugMessage":
                    if (reader.TokenType == JsonTokenType.String)
                        debugMessage = reader.GetString();
                    else
                        reader.Skip();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        // ParseElement requires ts, kind, code and params to all be present; otherwise null.
        if (ts == null || !sawKind || code == null || !sawParams)
            return null;

        return new PacketEntry(ts.Value, kind, code.Value, parameters!.Value, returnCode, debugMessage);
    }

    // Reads the "params" object: { "0": { "type": ..., "value": ... }, ... }
    private static ParamSet ReadParams(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return ParamSet.Empty;
        }

        var parameters = new List<KeyValuePair<string, ParamValue>>();

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            // Reuse a shared "0".."255" instance instead of allocating a fresh key string per packet.
            string name = ParamKeys.Intern(reader.GetString()!);
            reader.Read(); // advance to the param value (expected an object)

            string type = "";
            object? value = null;
            bool sawValue = false;

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
                {
                    string inner = reader.GetString()!;
                    reader.Read();
                    switch (inner)
                    {
                        case "type":
                            // Intern: a handful of distinct type names dedupe to single instances.
                            type = reader.TokenType == JsonTokenType.String ? string.Intern(reader.GetString() ?? "") : "";
                            break;
                        case "value":
                            value = ExtractValue(ref reader);
                            sawValue = true;
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
            }
            else
            {
                // Not an object: skip whatever it is, keep the type/value defaults.
                reader.Skip();
            }

            _ = sawValue; // value stays null when absent, matching ParseElement.
            Upsert(parameters, name, new ParamValue(type, value));
        }

        return new ParamSet(parameters.ToArray());
    }

    // Mirrors the old Dictionary indexer's last-write-wins: a repeated key overwrites in place
    // (duplicate keys within one packet's params object are not expected, but the behavior matches).
    private static void Upsert(List<KeyValuePair<string, ParamValue>> entries, string key, ParamValue value)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Key == key)
            {
                entries[i] = new KeyValuePair<string, ParamValue>(key, value);
                return;
            }
        entries.Add(new KeyValuePair<string, ParamValue>(key, value));
    }

    // Mirrors ExtractValue(JsonElement) exactly: Int64 first, then Double, String, Bool, Null,
    // Array (List<object?>), Object (Dictionary<string, object?>), else the raw token text.
    private static object? ExtractValue(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number when reader.TryGetInt64(out var l) => l,
        JsonTokenType.Number when reader.TryGetDouble(out var d) => d,
        JsonTokenType.String => reader.GetString(),
        JsonTokenType.True => true,
        JsonTokenType.False => false,
        JsonTokenType.Null => null,
        JsonTokenType.StartArray => ReadArray(ref reader),
        JsonTokenType.StartObject => ReadObject(ref reader),
        _ => TokenToString(ref reader)
    };

    private static List<object?> ReadArray(ref Utf8JsonReader reader)
    {
        var list = new List<object?>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            list.Add(ExtractValue(ref reader));
        return list;
    }

    private static Dictionary<string, object?> ReadObject(ref Utf8JsonReader reader)
    {
        var dict = new Dictionary<string, object?>();
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            string name = reader.GetString()!;
            reader.Read();
            dict[name] = ExtractValue(ref reader);
        }
        return dict;
    }

    // Fallback for token kinds the switch does not special-case (mirrors JsonElement.ToString()).
    private static string TokenToString(ref Utf8JsonReader reader)
    {
        if (reader.HasValueSequence)
            return Encoding.UTF8.GetString(System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence));
        return Encoding.UTF8.GetString(reader.ValueSpan);
    }

    private static PacketEntry? ParseElement(JsonElement root)
    {
        if (!root.TryGetProperty("ts", out var tsEl) ||
            !root.TryGetProperty("kind", out var kindEl) ||
            !root.TryGetProperty("code", out var codeEl) ||
            !root.TryGetProperty("params", out var paramsEl))
            return null;

        var ts = tsEl.GetDateTime();
        var kind = string.Intern(kindEl.GetString() ?? "");
        var code = codeEl.GetInt32();
        var parameters = new List<KeyValuePair<string, ParamValue>>();

        foreach (var param in paramsEl.EnumerateObject())
        {
            var type = param.Value.TryGetProperty("type", out var typeEl) ? string.Intern(typeEl.GetString() ?? "") : "";
            object? value = param.Value.TryGetProperty("value", out var valueEl) ? ExtractValue(valueEl) : null;
            Upsert(parameters, ParamKeys.Intern(param.Name), new ParamValue(type, value));
        }

        // Photon response framing (present only on RESPONSE packets we captured ourselves).
        int? returnCode = root.TryGetProperty("returnCode", out var rcEl) && rcEl.ValueKind == JsonValueKind.Number
            ? rcEl.GetInt32() : null;
        string? debugMessage = root.TryGetProperty("debugMessage", out var dmEl) && dmEl.ValueKind == JsonValueKind.String
            ? dmEl.GetString() : null;

        return new PacketEntry(ts, kind, code, new ParamSet(parameters.ToArray()), returnCode, debugMessage);
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
