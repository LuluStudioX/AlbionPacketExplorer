using AlbionPacketExplorer.Models;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public class PacketFileReader
{
    // Report progress at most this often so a multi-million line load does not flood the
    // dispatcher with callbacks; a final Report(1.0) always fires when the stream ends.
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(75);

    /// <summary>Entry-only stream for consumers that never touch params (merge/verify tool).</summary>
    public async IAsyncEnumerable<PacketEntry> ReadAsync(string filePath, IProgress<double>? progress = null, PackedParamStore? store = null)
    {
        await foreach (var (entry, _) in ReadWithParamsAsync(filePath, progress, store))
            yield return entry;
    }

    // store is optional and last: callers that retain packets (the main load path) pass their own
    // arena so decoded params stay reachable; one-shot consumers that stream-and-discard (e.g. the
    // merge/verify tool) omit it and get a private per-call store.
    /// <summary>
    /// Streams each packet together with its freshly parsed <see cref="ParamSet"/>. The load path
    /// consumes the set directly (aggregator ingest) instead of round-tripping through
    /// <see cref="PackedParamStore.Decode"/> for params it just encoded.
    /// </summary>
    public async IAsyncEnumerable<(PacketEntry Entry, ParamSet Params)> ReadWithParamsAsync(string filePath, IProgress<double>? progress = null, PackedParamStore? store = null)
    {
        store ??= new PackedParamStore();

        var fileInfo = new FileInfo(filePath);
        long totalBytes = fileInfo.Length;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        bool isArray = await IsJsonArrayAsync(stream);
        stream.Position = 0;

        if (isArray)
        {
            await foreach (var pair in ReadArrayAsync(stream, store, progress))
                yield return pair;
        }
        else
        {
            await foreach (var pair in ReadNdjsonAsync(stream, store, totalBytes, progress))
                yield return pair;
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

    // NDJSON: read raw byte chunks and split lines in the buffer, feeding each line's UTF-8 bytes
    // straight to Utf8JsonReader. The old StreamReader path decoded every line to a UTF-16 string and
    // re-encoded it back to UTF-8 for the parser - two transcodes plus one string allocation per line,
    // millions of times per big load. The buffer grows only if a single line outsizes it.
    private static async IAsyncEnumerable<(PacketEntry, ParamSet)> ReadNdjsonAsync(Stream stream, PackedParamStore store, long totalBytes, IProgress<double>? progress)
    {
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int dataLen = 0;
            int lineStart = 0;
            bool checkBom = true;

            while (true)
            {
                // A full buffer with no newline means one line outsizes it: grow and keep reading.
                if (dataLen == buffer.Length)
                {
                    byte[] bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    Buffer.BlockCopy(buffer, 0, bigger, 0, dataLen);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }

                int read = await stream.ReadAsync(buffer.AsMemory(dataLen, buffer.Length - dataLen));
                if (read == 0)
                {
                    // Final line without a trailing newline.
                    int tail = dataLen - lineStart;
                    if (tail > 0 && buffer[lineStart + tail - 1] == (byte)'\r') tail--;
                    if (tail > 0)
                    {
                        PacketEntry? entry = null;
                        ParamSet ps = ParamSet.Empty;
                        try { entry = ParseLine(buffer, lineStart, tail, store, out ps); } catch { }
                        if (entry != null) yield return (entry, ps);
                    }
                    break;
                }
                dataLen += read;

                if (checkBom && dataLen >= 3)
                {
                    // Skip a UTF-8 BOM (the old StreamReader absorbed it transparently).
                    if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                        lineStart = 3;
                    checkBom = false;
                }

                while (true)
                {
                    int nl = FindNewline(buffer, lineStart, dataLen - lineStart);
                    if (nl < 0) break;

                    int len = nl - lineStart;
                    if (len > 0 && buffer[lineStart + len - 1] == (byte)'\r') len--;
                    if (len > 0)
                    {
                        PacketEntry? entry = null;
                        ParamSet ps = ParamSet.Empty;
                        try { entry = ParseLine(buffer, lineStart, len, store, out ps); } catch { }
                        if (entry != null) yield return (entry, ps);
                    }
                    lineStart = nl + 1;
                }

                // Compact the consumed prefix so the buffer space recycles; the leftover is at most
                // one partial line.
                if (lineStart > 0)
                {
                    Buffer.BlockCopy(buffer, lineStart, buffer, 0, dataLen - lineStart);
                    dataLen -= lineStart;
                    lineStart = 0;
                }

                if (progress != null && totalBytes > 0)
                {
                    var now = sw.Elapsed;
                    if (now - lastReport >= ProgressInterval)
                    {
                        progress.Report((double)stream.Position / totalBytes);
                        lastReport = now;
                    }
                }
            }

            progress?.Report(1.0);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Span.IndexOf is vectorized; kept in a sync helper so the async iterator never holds a span.
    private static int FindNewline(byte[] buffer, int start, int count)
    {
        int i = buffer.AsSpan(start, count).IndexOf((byte)'\n');
        return i < 0 ? -1 : start + i;
    }

    private static async IAsyncEnumerable<(PacketEntry, ParamSet)> ReadArrayAsync(Stream stream, PackedParamStore store, IProgress<double>? progress)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Stream the array element-by-element so a giant array file is never fully materialized.
        await foreach (var el in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream, options))
        {
            PacketEntry? entry = null;
            ParamSet ps = ParamSet.Empty;
            try { entry = ParseElement(el, store, out ps); } catch { }
            if (entry != null) yield return (entry, ps);
        }

        progress?.Report(1.0);
    }

    // NDJSON parse: tokenize one line's UTF-8 bytes in place with Utf8JsonReader - no JsonDocument
    // DOM, no string round trip. Produces a PacketEntry byte-for-byte equivalent to ParseElement.
    // The freshly parsed ParamSet is also returned so the load path never decodes the store.
    private static PacketEntry? ParseLine(byte[] buf, int offset, int length, PackedParamStore store, out ParamSet paramSet)
    {
        paramSet = ParamSet.Empty;
        var reader = new Utf8JsonReader(buf.AsSpan(offset, length));

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

        // Property names are matched via ValueTextEquals so no per-property string is allocated.
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("ts"u8))
            {
                reader.Read();
                ts = reader.GetDateTime();
            }
            else if (reader.ValueTextEquals("kind"u8))
            {
                reader.Read();
                kind = ReadKind(ref reader);
                sawKind = true;
            }
            else if (reader.ValueTextEquals("code"u8))
            {
                reader.Read();
                code = reader.GetInt32();
            }
            else if (reader.ValueTextEquals("params"u8))
            {
                reader.Read();
                // Parse the params object to a ParamSet and pack it into the store, which
                // binary-encodes it into the arena. The source file is still JSON; only the
                // arena is binary. ReadParams advances the reader past the matching '}'.
                if (reader.TokenType == JsonTokenType.StartObject)
                {
                    paramSet = ParamCodec.ReadParams(ref reader);
                    paramRef = store.Append(paramSet);
                }
                else
                {
                    // params present but not an object: store nothing (decodes to empty).
                    reader.Skip();
                    paramRef = ParamRef.Empty;
                }
                sawParams = true;
            }
            else if (reader.ValueTextEquals("returnCode"u8))
            {
                reader.Read();
                // Photon response framing: number only (mirrors JsonValueKind.Number guard).
                if (reader.TokenType == JsonTokenType.Number)
                    returnCode = reader.GetInt32();
                else
                    reader.Skip();
            }
            else if (reader.ValueTextEquals("debugMessage"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String)
                    debugMessage = reader.GetString();
                else
                    reader.Skip();
            }
            else
            {
                // Skip on a PropertyName consumes the name and its whole value.
                reader.Skip();
            }
        }

        // ParseElement requires ts, kind, code and params to all be present; otherwise null.
        if (ts == null || !sawKind || code == null || !sawParams)
            return null;

        return new PacketEntry(ts.Value, kind, code.Value, store, paramRef, returnCode, debugMessage);
    }

    // The three Kind values are matched against the UTF-8 token directly (no alloc, no intern-table
    // lookup); anything unexpected falls back to the old GetString+Intern behavior.
    private static string ReadKind(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("EVENT"u8)) return "EVENT";
        if (reader.ValueTextEquals("REQUEST"u8)) return "REQUEST";
        if (reader.ValueTextEquals("RESPONSE"u8)) return "RESPONSE";
        return string.Intern(reader.GetString() ?? "");
    }

    private static PacketEntry? ParseElement(JsonElement root, PackedParamStore store, out ParamSet paramSet)
    {
        paramSet = ParamSet.Empty;

        if (!root.TryGetProperty("ts", out var tsEl) ||
            !root.TryGetProperty("kind", out var kindEl) ||
            !root.TryGetProperty("code", out var codeEl) ||
            !root.TryGetProperty("params", out var paramsEl))
            return null;

        var ts = tsEl.GetDateTime();
        var kind = string.Intern(kindEl.GetString() ?? "");
        var code = codeEl.GetInt32();

        // Parse the params object to a ParamSet via the shared ParamCodec (so it is value-identical
        // to the NDJSON path) and pack it into the store, which binary-encodes it into the arena.
        // GetRawText() yields valid UTF-8 JSON for the object; whitespace is irrelevant to the parser.
        ParamRef paramRef;
        if (paramsEl.ValueKind == JsonValueKind.Object)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(paramsEl.GetRawText());
            var reader = new Utf8JsonReader(bytes);
            reader.Read(); // position at StartObject
            paramSet = ParamCodec.ReadParams(ref reader);
            paramRef = store.Append(paramSet);
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
