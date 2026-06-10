using System;
using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>
/// Backs the diagnostics window: a live CPU/memory sample once a second, the last load's timing
/// breakdown, and a mirror of the <see cref="AppDiagnostics"/> log ring. The verbose toggle is the
/// user-facing "debug flag" - it turns on per-step load detail in the log.
/// </summary>
public sealed partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _timer;

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty] private string _memoryText = "-";
    [ObservableProperty] private string _cpuText = "-";
    [ObservableProperty] private string _gcText = "-";
    [ObservableProperty] private string _threadsText = "-";
    [ObservableProperty] private string _loadSummary = "No file loaded this session.";
    [ObservableProperty] private bool _verbose;

    public IClipboard? Clipboard { get; set; }
    public Action? RequestOpenLogsFolder { get; set; }

    public DiagnosticsViewModel()
    {
        foreach (var line in AppDiagnostics.Snapshot())
            Log.Add(line);
        _verbose = AppDiagnostics.VerboseEnabled;

        AppDiagnostics.Logged += OnLogged;
        AppDiagnostics.LoadReported += OnLoadReported;

        RefreshLoadSummary();
        Sample();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        _timer.Start();
    }

    partial void OnVerboseChanged(bool value) => AppDiagnostics.VerboseEnabled = value;

    private void OnLogged(string line) =>
        Dispatcher.UIThread.Post(() =>
        {
            Log.Add(line);
            while (Log.Count > AppDiagnostics.LogCap) Log.RemoveAt(0);
        });

    private void OnLoadReported(LoadReport _) =>
        Dispatcher.UIThread.Post(RefreshLoadSummary);

    private void RefreshLoadSummary()
    {
        if (AppDiagnostics.LastLoad is not { } r)
        {
            LoadSummary = "No file loaded this session.";
            return;
        }

        LoadSummary =
            $"{System.IO.Path.GetFileName(r.File)}\n"
          + $"{r.Packets:N0} packets   {Bytes(r.Bytes)}   {r.MbPerSec:F0} MB/s\n"
          + $"total {r.Total.TotalSeconds:F1}s\n"
          + $"  parse   {r.Parse.TotalSeconds:F1}s\n"
          + $"    reader  {r.Reader.TotalSeconds:F1}s  (worker parse + mmap append + entry build)\n"
          + $"    consume {r.Consume.TotalSeconds:F1}s  (stats ingest + correlate)\n"
          + $"  build   {r.Build.TotalSeconds:F1}s  (rows + grid)";
    }

    private void Sample()
    {
        var s = AppDiagnostics.Sample();
        MemoryText = $"working set {Bytes(s.WorkingSet)}   private {Bytes(s.PrivateBytes)}   "
                   + $"managed {Bytes(s.ManagedHeap)}   committed {Bytes(s.Committed)}\n"
                   + $"peak: working set {Bytes(AppDiagnostics.PeakWorkingSet)}   "
                   + $"private {Bytes(AppDiagnostics.PeakPrivateBytes)}   "
                   + $"managed {Bytes(AppDiagnostics.PeakManagedHeap)}";
        CpuText = $"{s.CpuPercent:F0}%   (peak {AppDiagnostics.PeakCpuPercent:F0}%)";
        GcText = $"gen0 {s.Gen0}   gen1 {s.Gen1}   gen2 {s.Gen2}";
        ThreadsText = s.Threads.ToString();
    }

    [RelayCommand]
    private void ClearLog()
    {
        AppDiagnostics.Clear();
        Log.Clear();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CopyLog()
    {
        if (Clipboard is null) return;
        var sb = new StringBuilder();
        foreach (var line in Log) sb.AppendLine(line);
        await Clipboard.SetTextAsync(sb.ToString());
    }

    [RelayCommand]
    private void ForceGc()
    {
        AppDiagnostics.Log("manual GC requested");
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        Sample();
    }

    [RelayCommand]
    private void OpenLogsFolder() => RequestOpenLogsFolder?.Invoke();

    public void Dispose()
    {
        _timer.Stop();
        AppDiagnostics.Logged -= OnLogged;
        AppDiagnostics.LoadReported -= OnLoadReported;
    }

    private static string Bytes(long b)
    {
        double mb = b / 1024.0 / 1024.0;
        return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F0} MB";
    }
}
