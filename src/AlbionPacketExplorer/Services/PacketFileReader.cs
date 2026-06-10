using AlbionPacketExplorer.Models;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

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

    // A parsed line before store packing. Workers produce these in parallel; the consumer appends
    // each ParamSet to the store and builds the PacketEntry, keeping the store single-writer.
    private readonly record struct ParsedLine(
        DateTime Ts, string Kind, int Code, ParamSet Params, int? ReturnCode, string? DebugMessage);

    // One worker result: the batch's parsed lines plus the stream position at the batch's end
    // (drives the progress bar from the consumer, so progress tracks ingest, not raw IO).
    private sealed record ParseBatchResult(List<ParsedLine> Lines, long Position);

    // NDJSON: a three-stage parallel pipeline.
    //   1. Producer reads raw byte chunks and slices each at its last newline into a pooled copy.
    //   2. Workers (plain Task.Run per batch) split lines and JSON-parse them concurrently - the
    //      expensive stage, and every line is independent.
    //   3. This iterator awaits the parse TASKS strictly in enqueue order, so output order matches
    //      file order with no reordering machinery, and store.Append plus the caller's ingest stay
    //      single-threaded. The bounded channel doubles as backpressure: at most `capacity` batches
    //      (~1 MB each) are in flight.
    // No per-line strings, no UTF-16 round trip: each line's UTF-8 bytes feed Utf8JsonReader as-is.
    private static async IAsyncEnumerable<(PacketEntry, ParamSet)> ReadNdjsonAsync(Stream stream, PackedParamStore store, long totalBytes, IProgress<double>? progress)
    {
        int capacity = Math.Clamp(Environment.ProcessorCount - 1, 2, 8);
        var channel = Channel.CreateBounded<Task<ParseBatchResult>>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        // Cancelled if the consumer abandons the iteration (exception or early break), so the
        // producer never deadlocks on a full channel nobody drains.
        using var cts = new CancellationTokenSource();

        var producer = Task.Run(async () =>
        {
            try
            {
                await ProduceBatchesAsync(stream, channel.Writer, cts.Token);
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        });

        try
        {
            var sw = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;

            await foreach (var batchTask in channel.Reader.ReadAllAsync())
            {
                var batch = await batchTask;
                foreach (var p in batch.Lines)
                {
                    // Pack the parsed set into the store, which binary-encodes it into the arena.
                    // The source file is still JSON; only the arena is binary.
                    var paramRef = store.Append(p.Params);
                    var entry = new PacketEntry(p.Ts, p.Kind, p.Code, store, paramRef, p.ReturnCode, p.DebugMessage);
                    yield return (entry, p.Params);
                }

                if (progress != null && totalBytes > 0)
                {
                    var now = sw.Elapsed;
                    if (now - lastReport >= ProgressInterval)
                    {
                        progress.Report((double)batch.Position / totalBytes);
                        lastReport = now;
                    }
                }
            }

            await producer;
            progress?.Report(1.0);
        }
        finally
        {
            cts.Cancel();
        }
    }

    // Stage 1: chunked reads, sliced at the last newline. The slice is copied into a pooled buffer
    // (the read buffer is reused immediately) and handed to a worker task; the partial tail line
    // moves to the buffer front. A cancelled read or write just ends the producer - the consumer
    // already stopped listening.
    private static async Task ProduceBatchesAsync(Stream stream, ChannelWriter<Task<ParseBatchResult>> writer, CancellationToken ct)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(1 << 20);
        try
        {
            int dataLen = 0;
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

                int read = await stream.ReadAsync(buffer.AsMemory(dataLen, buffer.Length - dataLen), ct);
                if (read == 0)
                {
                    // Final line without a trailing newline.
                    if (dataLen > 0)
                        await DispatchAsync(writer, buffer, 0, dataLen, stream.Position, ct);
                    break;
                }
                dataLen += read;

                int start = 0;
                if (checkBom && dataLen >= 3)
                {
                    // Skip a UTF-8 BOM (the old StreamReader absorbed it transparently).
                    if (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                        start = 3;
                    checkBom = false;
                }

                int lastNl = FindLastNewline(buffer, start, dataLen - start);
                if (lastNl < 0)
                {
                    // No complete line yet: drop a consumed BOM and keep accumulating.
                    if (start > 0)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, dataLen - start);
                        dataLen -= start;
                    }
                    continue;
                }

                int tail = dataLen - (lastNl + 1);
                await DispatchAsync(writer, buffer, start, lastNl + 1 - start, stream.Position - tail, ct);

                Buffer.BlockCopy(buffer, lastNl + 1, buffer, 0, tail);
                dataLen = tail;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask DispatchAsync(ChannelWriter<Task<ParseBatchResult>> writer,
        byte[] source, int offset, int length, long position, CancellationToken ct)
    {
        byte[] copy = ArrayPool<byte>.Shared.Rent(length);
        Buffer.BlockCopy(source, offset, copy, 0, length);
        await writer.WriteAsync(Task.Run(() => ParseBatch(copy, length, position), CancellationToken.None), ct);
    }

    // Stage 2 (worker): split the batch into lines and parse each. Always returns its pooled buffer.
    private static ParseBatchResult ParseBatch(byte[] buf, int length, long position)
    {
        try
        {
            var lines = new List<ParsedLine>(1024);
            int lineStart = 0;
            while (lineStart < length)
            {
                int nl = FindNewline(buf, lineStart, length - lineStart);
                int end = nl < 0 ? length : nl;
                int len = end - lineStart;
                if (len > 0 && buf[lineStart + len - 1] == (byte)'\r') len--;
                if (len > 0)
                {
                    // Malformed lines are skipped, matching the old per-line try/catch.
                    try { if (ParseLine(buf, lineStart, len) is { } p) lines.Add(p); } catch { }
                }
                if (nl < 0) break;
                lineStart = nl + 1;
            }
            return new ParseBatchResult(lines, position);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    // Span.IndexOf is vectorized; kept in sync helpers so the async methods never hold a span.
    private static int FindNewline(byte[] buffer, int start, int count)
    {
        int i = buffer.AsSpan(start, count).IndexOf((byte)'\n');
        return i < 0 ? -1 : start + i;
    }

    private static int FindLastNewline(byte[] buffer, int start, int count)
    {
        int i = buffer.AsSpan(start, count).LastIndexOf((byte)'\n');
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
    // DOM, no string round trip. Store-free so workers can run it in parallel; the consumer packs
    // the returned ParamSet into the store. Value-identical to ParseElement.
    private static ParsedLine? ParseLine(byte[] buf, int offset, int length)
    {
        var paramSet = ParamSet.Empty;
        var reader = new Utf8JsonReader(buf.AsSpan(offset, length));

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        DateTime? ts = null;
        bool sawKind = false;
        string kind = "";
        int? code = null;
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
                // ReadParams advances the reader past the matching '}'. A non-object params value
                // keeps the empty set (stores nothing, decodes to empty).
                if (reader.TokenType == JsonTokenType.StartObject)
                    paramSet = ParamCodec.ReadParams(ref reader);
                else
                    reader.Skip();
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

        return new ParsedLine(ts.Value, kind, code.Value, paramSet, returnCode, debugMessage);
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
