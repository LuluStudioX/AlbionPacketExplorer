using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AlbionPacketExplorer.Services;

/// <summary>One point-in-time process metrics sample (raw bytes/counts; formatting lives in the VM).</summary>
public readonly record struct ProcSample(
    long WorkingSet, long PrivateBytes, long ManagedHeap, long Committed,
    int Gen0, int Gen1, int Gen2, int Threads, double CpuPercent);

/// <summary>Timed breakdown of one file load, surfaced in the diagnostics window.</summary>
public sealed record LoadReport(
    string File, long Bytes, int Packets,
    TimeSpan Total, TimeSpan Parse, TimeSpan Reader, TimeSpan Consume, TimeSpan Build)
{
    public double MbPerSec => Parse.TotalSeconds > 0 ? Bytes / 1024.0 / 1024.0 / Parse.TotalSeconds : 0;
}

/// <summary>
/// Process-wide diagnostics sink: a bounded log ring, the last load's timing report, and a CPU/memory
/// sampler. Always compiled (works in Release so the shipped GHCR build is diagnosable); the verbose
/// flag only gates the chatty per-step lines. Thread-safe - producers run on background load workers.
/// </summary>
public static class AppDiagnostics
{
    public const int LogCap = 2000;

    private static readonly object _lock = new();
    private static readonly Queue<string> _log = new();

    /// <summary>The "debug flag": when on, <see cref="Verbose"/> lines are recorded; off by default.</summary>
    public static bool VerboseEnabled { get; set; }

    public static LoadReport? LastLoad { get; private set; }

    /// <summary>Raised (possibly off the UI thread) for each appended line; the VM marshals it.</summary>
    public static event Action<string>? Logged;
    public static event Action<LoadReport>? LoadReported;

    public static IReadOnlyList<string> Snapshot()
    {
        lock (_lock) return _log.ToArray();
    }

    public static void Clear()
    {
        lock (_lock) _log.Clear();
    }

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        lock (_lock)
        {
            _log.Enqueue(line);
            while (_log.Count > LogCap) _log.Dequeue();
        }
        Logged?.Invoke(line);
    }

    /// <summary>Records the line only when the verbose flag is on (per-step load detail, GC notes, ...).</summary>
    public static void Verbose(string message)
    {
        if (VerboseEnabled) Log(message);
    }

    public static void ReportLoad(LoadReport report)
    {
        LastLoad = report;
        Sample(); // record the post-load memory level into the peaks even when no window is sampling
        Log($"load: {report.Packets:N0} packets, {report.Bytes / 1024.0 / 1024.0:F0} MB in "
          + $"{report.Total.TotalSeconds:F1}s (parse {report.Parse.TotalSeconds:F1}s "
          + $"[reader {report.Reader.TotalSeconds:F1}s, consume {report.Consume.TotalSeconds:F1}s], "
          + $"build {report.Build.TotalSeconds:F1}s, {report.MbPerSec:F0} MB/s)");
        LoadReported?.Invoke(report);
    }

    // Session peaks, updated on every Sample() (the diagnostics window samples 1/s while open, and
    // the load path samples at completion). Reset only by process restart.
    public static long PeakWorkingSet { get; private set; }
    public static long PeakPrivateBytes { get; private set; }
    public static long PeakManagedHeap { get; private set; }
    public static double PeakCpuPercent { get; private set; }

    // CPU% is derived from the delta of total processor time over wall time since the last sample,
    // normalized by core count so 100% == all cores saturated.
    private static DateTime _lastWall = DateTime.UtcNow;
    private static TimeSpan _lastCpu = TimeSpan.Zero;

    public static ProcSample Sample()
    {
        using var p = Process.GetCurrentProcess();
        var now = DateTime.UtcNow;
        var cpu = p.TotalProcessorTime;

        double cpuPercent = 0;
        var wallMs = (now - _lastWall).TotalMilliseconds;
        if (wallMs > 0)
        {
            var usedMs = (cpu - _lastCpu).TotalMilliseconds;
            cpuPercent = Math.Clamp(usedMs / (wallMs * Environment.ProcessorCount) * 100.0, 0, 100);
        }
        _lastWall = now;
        _lastCpu = cpu;

        var gc = GC.GetGCMemoryInfo();
        var sample = new ProcSample(
            WorkingSet: p.WorkingSet64,
            PrivateBytes: p.PrivateMemorySize64,
            ManagedHeap: GC.GetTotalMemory(false),
            Committed: gc.TotalCommittedBytes,
            Gen0: GC.CollectionCount(0),
            Gen1: GC.CollectionCount(1),
            Gen2: GC.CollectionCount(2),
            Threads: p.Threads.Count,
            CpuPercent: cpuPercent);

        if (sample.WorkingSet > PeakWorkingSet) PeakWorkingSet = sample.WorkingSet;
        if (sample.PrivateBytes > PeakPrivateBytes) PeakPrivateBytes = sample.PrivateBytes;
        if (sample.ManagedHeap > PeakManagedHeap) PeakManagedHeap = sample.ManagedHeap;
        if (sample.CpuPercent > PeakCpuPercent) PeakCpuPercent = sample.CpuPercent;

        return sample;
    }
}
