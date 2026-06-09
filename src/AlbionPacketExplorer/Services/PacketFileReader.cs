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

    // store is optional and last: callers that retain packets (the main load path) pass their own
    // arena so decoded params stay reachable; one-shot consumers that stream-and-discard (e.g. the
    // merge/verify tool) omit it and get a private per-call store. Keeping it last leaves the
    // existing (path, progress) call sites compiling unchanged.
    public async IAsyncEnumerable<PacketEntry> ReadAsync(string filePath, IProgress<double>? progress = null, PackedParamStore? store = null)
    {
        store ??= new PackedParamStore();

        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        bool isArray = await IsJsonArrayAsync(stream);
        stream.Position = 0;

        if (isArray)
        {
            await foreach (var entry in ReadArrayAsync(stream, store, progress))
                yield return entry;
        }
        else
        {
            await foreach (var entry in ReadNdjsonAsync(stream, store, totalBytes, progress))
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

    private static async IAsyncEnumerable<PacketEntry> ReadNdjsonAsync(Stream stream, PackedParamStore store, long totalBytes, IProgress<double>? progress)
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
            try { entry = ParseLine(line, store); } catch { }
            if (entry != null) yield return entry;
        }

        progress?.Report(1.0);
    }

    private static async IAsyncEnumerable<PacketEntry> ReadArrayAsync(Stream stream, PackedParamStore store, IProgress<double>? progress)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Stream the array element-by-element so a giant array file is never fully materialized.
        await foreach (var el in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options))
        {
            PacketEntry? entry = null;
            try { entry = ParseElement(el, store); } catch { }
            if (entry != null) yield return entry;
        }

        progress?.Report(1.0);
    }

    // NDJSON parse: tokenize one line's UTF-8 bytes with Utf8JsonReader instead of building a
    // JsonDocument DOM per line. Produces a PacketEntry byte-for-byte equivalent to ParseElement.
    private static PacketEntry? ParseLine(string line, PackedParamStore store)
    {
        // Rent a scratch UTF-8 buffer from the shared pool instead of allocating a throwaway byte[]
        // per line (~millions of arrays per big load). Retained data (GetString() results) is COPIED
        // out during parse; the params bytes are copied into the store. The Utf8JsonReader is a local
        // ref struct used only here. The buffer is always returned (finally).
        int max = Encoding.UTF8.GetMaxByteCount(line.Length);
        byte[] buf = System.Buffers.ArrayPool<byte>.Shared.Rent(max);
        try
        {
            int n = Encoding.UTF8.GetBytes(line, buf);
            var reader = new Utf8JsonReader(buf.AsSpan(0, n));

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                return null;

            DateTime? ts = null;
            bool sawKind = false;
            string kind = "";
            int? code = null;
            ParamRef paramRef = ParamRef.Empty;
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
                        // Capture the params object's EXACT raw UTF-8 bytes (from its '{' to just past
                        // its '}') and pack them into the store. We still parse it once here only to
                        // (a) get the param count and (b) advance the reader past the object; the
                        // parsed set is discarded - the stored bytes re-decode identically on demand.
                        if (reader.TokenType == JsonTokenType.StartObject)
                        {
                            int start = (int)reader.TokenStartIndex;
                            var parsed = ParamCodec.ReadParams(ref reader); // advances to matching EndObject
                            int end = (int)reader.BytesConsumed;            // index just past '}'
                            paramRef = store.Append(buf.AsSpan(start, end - start), parsed.Count);
                        }
                        else
                        {
                            // params present but not an object: store nothing (decodes to empty).
                            reader.Skip();
                            paramRef = ParamRef.Empty;
                        }
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

            return new PacketEntry(ts.Value, kind, code.Value, store, paramRef, returnCode, debugMessage);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buf);
        }
    }

    private static PacketEntry? ParseElement(JsonElement root, PackedParamStore store)
    {
        if (!root.TryGetProperty("ts", out var tsEl) ||
            !root.TryGetProperty("kind", out var kindEl) ||
            !root.TryGetProperty("code", out var codeEl) ||
            !root.TryGetProperty("params", out var paramsEl))
            return null;

        var ts = tsEl.GetDateTime();
        var kind = string.Intern(kindEl.GetString() ?? "");
        var code = codeEl.GetInt32();

        // Capture the params object's raw JSON and pack it into the store; it re-decodes through the
        // shared ParamCodec.ReadParams to the same ParamSet the NDJSON path produces. GetRawText()
        // yields valid UTF-8 JSON for the object; whitespace is irrelevant to the decoder.
        ParamRef paramRef;
        if (paramsEl.ValueKind == JsonValueKind.Object)
        {
            int count = 0;
            foreach (var _ in paramsEl.EnumerateObject()) count++;
            byte[] bytes = Encoding.UTF8.GetBytes(paramsEl.GetRawText());
            paramRef = store.Append(bytes, count);
        }
        else
        {
            paramRef = ParamRef.Empty;
        }

        // Photon response framing (present only on RESPONSE packets we captured ourselves).
        int? returnCode = root.TryGetProperty("returnCode", out var rcEl) && rcEl.ValueKind == JsonValueKind.Number
            ? rcEl.GetInt32() : null;
        string? debugMessage = root.TryGetProperty("debugMessage", out var dmEl) && dmEl.ValueKind == JsonValueKind.String
            ? dmEl.GetString() : null;

        return new PacketEntry(ts, kind, code, store, paramRef, returnCode, debugMessage);
    }
}
