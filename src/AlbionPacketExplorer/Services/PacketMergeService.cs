using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using AlbionPacketExplorer.Models;

namespace AlbionPacketExplorer.Services;

/// <summary>Per-input outcome of a merge: how many packets it contributed (or why it failed).</summary>
public sealed record MergeFileResult(string Path, string Name, long Emitted, string? Error);

/// <summary>Aggregate result of a merge run.</summary>
public sealed record MergeResult(
    IReadOnlyList<MergeFileResult> Files, long TotalEmitted, string OutputPath, long OutputBytes);

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

    public async Task<MergeResult> MergeAsync(
        IReadOnlyList<string> inputs, string outputPath,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var reader = new PacketFileReader();
        var results = new List<MergeFileResult>(inputs.Count);
        long totalEmitted = 0;

        long totalBytes = 0;
        foreach (var p in inputs)
            try { totalBytes += new FileInfo(p).Length; } catch { /* unreadable -> ignored in total */ }
        if (totalBytes <= 0) totalBytes = 1;
        long doneBytes = 0;

        await using (var outStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write,
                         FileShare.None, bufferSize: 1 << 16, useAsync: true))
        await using (var writer = new StreamWriter(outStream, new UTF8Encoding(false)) { NewLine = "\n" })
        {
            foreach (var input in inputs)
            {
                ct.ThrowIfCancellationRequested();

                long fileBytes = 1;
                try { fileBytes = Math.Max(1, new FileInfo(input).Length); } catch { /* keep 1 */ }
                long start = doneBytes;

                long emitted = 0;
                string? error = null;
                try
                {
                    var fileProgress = new Progress<double>(p =>
                        progress?.Report(Math.Min(1.0, (start + p * fileBytes) / (double)totalBytes)));

                    await foreach (var packet in reader.ReadAsync(input, fileProgress))
                    {
                        ct.ThrowIfCancellationRequested();
                        await writer.WriteLineAsync(JsonSerializer.Serialize(PacketWire.ToJsonShape(packet), CompactJson));
                        emitted++;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { error = ex.Message; }

                doneBytes += fileBytes;
                progress?.Report(Math.Min(1.0, doneBytes / (double)totalBytes));
                totalEmitted += emitted;
                results.Add(new MergeFileResult(input, Path.GetFileName(input), emitted, error));
            }

            await writer.FlushAsync(ct);
        }

        long outBytes = 0;
        try { outBytes = new FileInfo(outputPath).Length; } catch { /* report 0 */ }
        return new MergeResult(results, totalEmitted, outputPath, outBytes);
    }

    /// <param name="sources">
    /// The inputs that produced <paramref name="outputPath"/>. When non-empty they are re-parsed
    /// from scratch and their total must equal the merged count - this is what proves nothing was
    /// dropped. Pass null/empty to verify a standalone file (internal consistency only).
    /// </param>
    public async Task<VerifyResult> VerifyAsync(
        string outputPath, IReadOnlyList<string>? sources,
        IProgress<double>? progress = null, CancellationToken ct = default)
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

            if (sourceTotal != readBack)
                issues.Add($"Sources hold {sourceTotal:N0} packets but merged has {readBack:N0}.");
        }

        if (lineCount != readBack)
            issues.Add($"Output has {lineCount:N0} lines but {readBack:N0} re-parsed packets.");

        progress?.Report(1.0);
        return new VerifyResult(lineCount, readBack, sourceTotal, sourcesChecked, issues.Count == 0, issues);
    }
}
