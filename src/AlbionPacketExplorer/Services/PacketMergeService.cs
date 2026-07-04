using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>Per-input outcome of a merge: how many packets it contributed (or why it failed), and
/// how many of those had their opcode normalized from an older protocol era to the current one.</summary>
public sealed record MergeFileResult(string Path, string Name, long Emitted, string? Error, long Remapped = 0);

/// <summary>Aggregate result of a merge run.</summary>
public sealed record MergeResult(
    IReadOnlyList<MergeFileResult> Files, long TotalEmitted, string OutputPath, long OutputBytes,
    long DuplicatesRemoved);

/// <summary>
/// Result of verifying a merged file. <see cref="MergedLineCount"/> and <see cref="ReadBackCount"/>
/// come from the output itself; when sources are supplied they are independently re-counted into
/// <see cref="SourceTotal"/> so a mismatch proves a packet was lost. <see cref="Ok"/> is the
/// go/no-go a caller gates a delete on.
/// </summary>
public sealed record VerifyResult(
    long MergedLineCount, long ReadBackCount, long SourceTotal, bool SourcesChecked,
    bool Ok, IReadOnlyList<string> Issues);

/// <summary>
/// Native merge/verify for packet captures - no external runtime. Reads every input through the
/// app's own <see cref="PacketFileReader"/> (handles both pretty JSON arrays and NDJSON), then
/// writes one compact packet per line to a single NDJSON file. Verify re-reads that output
/// end-to-end and confirms the packet count round-trips, catching truncation or corruption.
/// </summary>
public sealed class PacketMergeService
{
    private static readonly JsonSerializerOptions CompactJson = new()
    {
        WriteIndented = false,
        // Keep base64 '+' '/' and other payload chars literal so the output matches the wire form.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <param name="dedupe">
    /// When true, exact-duplicate packets are collapsed to a single line. Two packets are duplicates
    /// iff their canonical serialized form (the very bytes written here) is identical, so re-merging
    /// outputs that share an original capture cannot double its packets. The first occurrence in
    /// input order is kept; later identical lines are dropped and counted.
    /// </param>
    // Codes are sampled from at most this many packets to detect an unstamped capture's era before
    // normalizing; a stamped capture (sidecar) skips the sample entirely.
    private const int EraSampleCap = 200_000;

    public async Task<MergeResult> MergeAsync(
        IReadOnlyList<string> inputs, string outputPath, bool dedupe = true,
        IProgress<double>? progress = null, CancellationToken ct = default, bool normalize = true)
    {
        var reader = new PacketFileReader();
        var snapshots = new ProtocolSnapshotStore();
        var results = new List<MergeFileResult>(inputs.Count);
        long totalEmitted = 0;
        long duplicatesRemoved = 0;

        // 128-bit content hashes of every line already written (dedupe only). A hash set of 16-byte
        // keys costs ~16 B/packet versus retaining the full ~hundreds-of-bytes line, and the 128-bit
        // width makes a false-positive collision (which would wrongly drop a packet) negligible.
        var seen = dedupe ? new HashSet<UInt128>() : null;

        long totalBytes = 0;
        foreach (var p in inputs)
            try { totalBytes += new FileInfo(p).Length; } catch { /* unreadable -> ignored in total */ }
        if (totalBytes <= 0) totalBytes = 1;
        long doneBytes = 0;

        await using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
                         FileShare.None, bufferSize: 1 << 16, useAsync: true))
        {
            foreach (var input in inputs)
            {
                ct.ThrowIfCancellationRequested();

                long fileBytes = 1;
                try { fileBytes = Math.Max(1, new FileInfo(input).Length); } catch { /* keep 1 */ }
                long start = doneBytes;

                long emitted = 0;
                long remapped = 0;
                string? error = null;
                try
                {
                    var fileProgress = new Progress<double>(p =>
                        progress?.Report(Math.Min(1.0, (start + p * fileBytes) / (double)totalBytes)));

                    // Detect the era this input was captured under and build its code-normalization
                    // map (absent when it is already the current era). A stamped capture reads its
                    // sidecar; an unstamped one samples its codes to detect the era.
                    Dictionary<(string Kind, int Code), int>? codeMap = null;
                    if (normalize)
                    {
                        var era = snapshots.EraFromSidecar(input);
                        if (era is null)
                        {
                            var observed = new HashSet<(string, int)>();
                            int sampled = 0;
                            await foreach (var e in reader.ReadAsync(input))
                            {
                                observed.Add((e.Kind, e.Code));
                                if (++sampled >= EraSampleCap) break;
                                ct.ThrowIfCancellationRequested();
                            }
                            era = snapshots.DetectEra(observed);
                        }
                        if (era is not null)
                        {
                            var map = ProtocolSnapshotStore.CanonicalRemap(era);
                            if (map.Count > 0) codeMap = map;
                        }
                    }

                    // Read the file as ordered batches (the reader parses in parallel). For each batch
                    // the CPU-heavy work - shaping every packet to its canonical UTF-8 JSON line and
                    // hashing it - runs across all cores; the serial tail only does the dedupe set
                    // check and one buffered write per batch, preserving input order exactly.
                    await foreach (var batch in reader.ReadBatchesAsync(input, fileProgress))
                    {
                        ct.ThrowIfCancellationRequested();
                        int n = batch.Items.Count;
                        if (n == 0) continue;

                        var lines = new byte[n][];
                        var hashes = seen is not null ? new UInt128[n] : null;
                        Parallel.For(0, n, i =>
                        {
                            var (entry, ps) = batch.Items[i];
                            int code = entry.Code;
                            if (codeMap is not null &&
                                codeMap.TryGetValue((entry.Kind, entry.Code), out var nc) && nc != code)
                            {
                                code = nc;
                                System.Threading.Interlocked.Increment(ref remapped);
                            }
                            byte[] line = JsonSerializer.SerializeToUtf8Bytes(
                                PacketWire.ToJsonShape(entry, ps, code), CompactJson);
                            lines[i] = line;
                            if (hashes is not null) hashes[i] = Hash(line);
                        });

                        using var chunk = new MemoryStream(n * 64);
                        for (int i = 0; i < n; i++)
                        {
                            if (seen is not null && !seen.Add(hashes![i]))
                            {
                                duplicatesRemoved++;
                                continue;
                            }
                            chunk.Write(lines[i], 0, lines[i].Length);
                            chunk.WriteByte((byte)'\n');
                            emitted++;
                        }
                        if (chunk.Length > 0)
                            await outStream.WriteAsync(chunk.GetBuffer().AsMemory(0, (int)chunk.Length), ct);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { error = ex.Message; }

                doneBytes += fileBytes;
                progress?.Report(Math.Min(1.0, doneBytes / (double)totalBytes));
                totalEmitted += emitted;
                results.Add(new MergeFileResult(input, Path.GetFileName(input), emitted, error, remapped));
            }

            await outStream.FlushAsync(ct);
        }

        // The merged output is now entirely in the app's current code space; stamp it canonical so
        // reopening it needs no era remap.
        if (normalize) snapshots.StampCanonical(outputPath);

        long outBytes = 0;
        try { outBytes = new FileInfo(outputPath).Length; } catch { /* report 0 */ }
        return new MergeResult(results, totalEmitted, outputPath, outBytes, duplicatesRemoved);
    }

    // Content hash for dedupe: MD5 is not used for security here, only as a fast, dependency-free
    // 128-bit digest. TryHashData writes into a stack buffer so per-packet hashing never allocates.
    private static UInt128 Hash(ReadOnlySpan<byte> data)
    {
        Span<byte> digest = stackalloc byte[16];
        System.Security.Cryptography.MD5.HashData(data, digest);
        ulong lo = BitConverter.ToUInt64(digest[..8]);
        ulong hi = BitConverter.ToUInt64(digest[8..]);
        return new UInt128(hi, lo);
    }

    /// <param name="sources">
    /// The inputs that produced <paramref name="outputPath"/>. When non-empty they are re-parsed
    /// from scratch and their total, minus <paramref name="duplicatesRemoved"/>, must equal the
    /// merged count - this is what proves nothing was dropped. Pass null/empty to verify a standalone
    /// file (internal consistency only).
    /// </param>
    /// <param name="duplicatesRemoved">
    /// How many exact-duplicate packets the merge collapsed. The re-counted source total is expected
    /// to exceed the merged count by exactly this much (dedupe removes, never adds), so the cross
    /// check stays a real "nothing lost" guarantee even with dedupe on.
    /// </param>
    public async Task<VerifyResult> VerifyAsync(
        string outputPath, IReadOnlyList<string>? sources,
        IProgress<double>? progress = null, CancellationToken ct = default, long duplicatesRemoved = 0)
    {
        var issues = new List<string>();
        var reader = new PacketFileReader();
        bool sourcesChecked = sources is { Count: > 0 };
        double outShare = sourcesChecked ? 0.5 : 1.0;

        // Pass 1: raw line count of the output (cheap, no progress).
        long lineCount = 0;
        await using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                         1 << 16, useAsync: true))
        using (var sr = new StreamReader(fs, Encoding.UTF8))
        {
            string? line;
            while ((line = await sr.ReadLineAsync(ct)) != null)
                if (!string.IsNullOrWhiteSpace(line)) lineCount++;
        }

        // Pass 2: re-parse the output end-to-end.
        long readBack = 0;
        var outProgress = new Progress<double>(p => progress?.Report(p * outShare));
        await foreach (var _ in reader.ReadAsync(outputPath, outProgress))
        {
            ct.ThrowIfCancellationRequested();
            readBack++;
        }

        // Pass 3: independently re-count every source so the merged total can be cross-checked.
        long sourceTotal = readBack;
        if (sourcesChecked)
        {
            long totalBytes = 0;
            foreach (var s in sources!)
                try { totalBytes += new FileInfo(s).Length; } catch { /* ignored */ }
            if (totalBytes <= 0) totalBytes = 1;

            long doneBytes = 0;
            sourceTotal = 0;
            foreach (var src in sources!)
            {
                ct.ThrowIfCancellationRequested();
                long fileBytes = 1;
                try { fileBytes = Math.Max(1, new FileInfo(src).Length); } catch { /* keep 1 */ }
                long start = doneBytes;
                var srcProgress = new Progress<double>(p =>
                    progress?.Report(0.5 + Math.Min(1.0, (start + p * fileBytes) / (double)totalBytes) * 0.5));

                try
                {
                    await foreach (var _ in reader.ReadAsync(src, srcProgress))
                    {
                        ct.ThrowIfCancellationRequested();
                        sourceTotal++;
                    }
                }
                catch (Exception ex) { issues.Add($"Could not re-read {Path.GetFileName(src)}: {ex.Message}"); }

                doneBytes += fileBytes;
            }

            if (sourceTotal - duplicatesRemoved != readBack)
                issues.Add(duplicatesRemoved > 0
                    ? $"Sources hold {sourceTotal:N0} packets, merged has {readBack:N0} after removing "
                      + $"{duplicatesRemoved:N0} duplicate(s) - expected {sourceTotal - duplicatesRemoved:N0}."
                    : $"Sources hold {sourceTotal:N0} packets but merged has {readBack:N0}.");
        }

        if (lineCount != readBack)
            issues.Add($"Output has {lineCount:N0} lines but {readBack:N0} re-parsed packets.");

        progress?.Report(1.0);
        return new VerifyResult(lineCount, readBack, sourceTotal, sourcesChecked, issues.Count == 0, issues);
    }
}
