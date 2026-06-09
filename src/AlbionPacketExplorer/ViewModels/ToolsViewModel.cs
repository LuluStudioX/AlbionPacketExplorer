using System.Collections.ObjectModel;
using System.Collections.Specialized;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>One input capture queued for merging, with its post-merge contribution shown inline.</summary>
public partial class ToolsInputItem : ObservableObject
{
    public string Path { get; }
    public string Name { get; }
    public string SizeText { get; }

    /// <summary>Filled after a merge: emitted packet count, or an error string.</summary>
    [ObservableProperty] private string _status = "";

    public ToolsInputItem(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        long len = 0;
        try { len = new FileInfo(path).Length; } catch { /* unreadable -> 0 */ }
        SizeText = FormatBytes(len);
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return u == 0 ? $"{size:0} {units[u]}" : $"{size:0.#} {units[u]}";
    }
}

/// <summary>
/// Drives the Tools window: a Merge -> Verify -> Continue flow over a list of packet captures.
/// File/folder/output pickers are supplied by the view (it owns the StorageProvider); the
/// Continue step hands the merged path back so the main explorer can load it.
/// </summary>
public partial class ToolsViewModel : ObservableObject
{
    private readonly PacketMergeService _merge = new();

    public ObservableCollection<ToolsInputItem> Inputs { get; } = [];

    /// <summary>True while at least one input is queued (drives the empty-state placeholder).</summary>
    public bool HasInputs => Inputs.Count > 0;

    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isBusy;

    /// <summary>True once a merge has completed without throwing (gates Verify).</summary>
    [ObservableProperty] private bool _merged;

    /// <summary>True once Verify confirmed the merged file is intact (gates Continue).</summary>
    [ObservableProperty] private bool _verified;

    [ObservableProperty] private string _mergeSummary = "";
    [ObservableProperty] private string _verifySummary = "";
    [ObservableProperty] private string _log = "";

    /// <summary>Supplied by the view: multi-select capture picker. Returns picked local paths.</summary>
    public Func<Task<IReadOnlyList<string>>>? RequestPickFiles;

    /// <summary>Supplied by the view: folder picker. Returns the folder path or null.</summary>
    public Func<Task<string?>>? RequestPickFolder;

    /// <summary>Supplied by the view: output save picker, seeded with a suggested name.</summary>
    public Func<string, Task<string?>>? RequestPickOutput;

    /// <summary>Supplied by the view: open picker for an existing merged file to verify/load.</summary>
    public Func<Task<string?>>? RequestPickExisting;

    /// <summary>Raised by Load with the merged output path so the explorer can open it.</summary>
    public event Action<string>? LoadRequested;

    public ToolsViewModel()
    {
        Inputs.CollectionChanged += OnInputsChanged;
    }

    public ToolsViewModel(IEnumerable<string> seedPaths) : this()
    {
        AddPaths(seedPaths);
    }

    private void OnInputsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Any change to the queue invalidates a prior merge/verify result.
        Merged = false;
        Verified = false;
        OnPropertyChanged(nameof(HasInputs));
        MergeCommand.NotifyCanExecuteChanged();
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Inputs.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p) || !existing.Add(p)) continue;
            if (!File.Exists(p)) continue;
            Inputs.Add(new ToolsInputItem(p));
        }

        // Default the output next to the first input the first time something is queued.
        if (string.IsNullOrWhiteSpace(OutputPath) && Inputs.Count > 0)
        {
            var dir = Path.GetDirectoryName(Inputs[0].Path);
            if (!string.IsNullOrEmpty(dir))
                OutputPath = Path.Combine(dir, "packets-merged.json");
        }
    }

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        if (RequestPickFiles is null) return;
        AddPaths(await RequestPickFiles());
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        if (RequestPickFolder is null) return;
        var folder = await RequestPickFolder();
        if (string.IsNullOrWhiteSpace(folder)) return;

        var outName = string.IsNullOrWhiteSpace(OutputPath) ? null : Path.GetFileName(OutputPath);
        var jsons = Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
            .Where(p => !string.Equals(Path.GetFileName(p), outName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        AddPaths(jsons);
    }

    [RelayCommand]
    private void RemoveInput(ToolsInputItem? item)
    {
        if (item is not null) Inputs.Remove(item);
    }

    [RelayCommand]
    private void ClearInputs() => Inputs.Clear();

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        if (RequestPickOutput is null) return;
        var suggested = string.IsNullOrWhiteSpace(OutputPath) ? "packets-merged.json" : Path.GetFileName(OutputPath);
        var picked = await RequestPickOutput(suggested);
        if (!string.IsNullOrWhiteSpace(picked)) OutputPath = picked;
    }

    /// <summary>Point at an existing merged file to verify or load on its own (no merge needed).</summary>
    [RelayCommand]
    private async Task OpenExistingAsync()
    {
        if (RequestPickExisting is null) return;
        var picked = await RequestPickExisting();
        if (string.IsNullOrWhiteSpace(picked)) return;
        // Standalone target: no known sources, so Verify checks internal consistency only.
        OutputPath = picked;
        Log = "";
        MergeSummary = "";
        VerifySummary = "";
    }

    // Merging one file is a no-op copy, so require at least two inputs.
    private bool CanMerge() => !IsBusy && Inputs.Count >= 2 && !string.IsNullOrWhiteSpace(OutputPath);

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private async Task MergeAsync()
    {
        IsBusy = true;
        Merged = false;
        Verified = false;
        Progress = 0;
        VerifySummary = "";
        MergeSummary = "";
        Log = "";
        AppendLog($"Merging {Inputs.Count} file(s) -> {OutputPath}");
        foreach (var i in Inputs) i.Status = "";

        var inputs = Inputs.Select(i => i.Path).ToList();
        var byPath = Inputs.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);
        var progress = new Progress<double>(p => Progress = p);

        try
        {
            var r = await Task.Run(() => _merge.MergeAsync(inputs, OutputPath, progress));

            int errored = 0;
            foreach (var f in r.Files)
            {
                if (byPath.TryGetValue(f.Path, out var item))
                    item.Status = f.Error is null ? $"{f.Emitted:N0}" : "skip";
                if (f.Error is null)
                    AppendLog($"  {f.Emitted,12:N0}  {f.Name}");
                else
                {
                    errored++;
                    AppendLog($"  {"error",12}  {f.Name}: {f.Error}");
                }
            }

            MergeSummary = $"{r.TotalEmitted:N0} packets -> {Path.GetFileName(r.OutputPath)} " +
                           $"({ToolsInputItem.FormatBytes(r.OutputBytes)})";
            AppendLog(errored == 0
                ? $"Merge done. {MergeSummary}"
                : $"Merge done with {errored} unreadable file(s). {MergeSummary}");
            Merged = true;

            // Verify always runs right after a merge, cross-checking against the very inputs that
            // produced the file, so the delete prompt only appears once nothing-was-lost is proven.
            await RunVerifyAsync(inputs);
        }
        catch (Exception ex)
        {
            AppendLog($"Merge failed: {ex.Message}");
            MergeSummary = "Merge failed.";
        }
        finally
        {
            Progress = 1;
            IsBusy = false;
        }
    }

    // Verify on its own: double-checks the current output, or a standalone file chosen via Open.
    private bool CanVerify() => !IsBusy && !string.IsNullOrWhiteSpace(OutputPath) && File.Exists(OutputPath);

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        IsBusy = true;
        Progress = 0;
        // Standalone verify has no known sources, so it checks internal consistency only and never
        // offers to delete anything.
        try { await RunVerifyAsync(null); }
        finally
        {
            Progress = 1;
            IsBusy = false;
        }
    }

    // Shared verify body. Caller owns IsBusy/Progress so it can run standalone or chained after merge.
    private async Task RunVerifyAsync(IReadOnlyList<string>? sources)
    {
        Verified = false;
        AppendLog($"Verifying {Path.GetFileName(OutputPath)} ...");
        var progress = new Progress<double>(p => Progress = p);

        try
        {
            var r = await Task.Run(() => _merge.VerifyAsync(OutputPath, sources, progress));
            if (r.Ok)
            {
                VerifySummary = r.SourcesChecked
                    ? $"OK - {r.ReadBackCount:N0} packets, matches all sources. Nothing lost."
                    : $"OK - {r.ReadBackCount:N0} packets re-read, lines match.";
                AppendLog(r.SourcesChecked
                    ? $"Verify OK: {r.MergedLineCount:N0} lines, {r.ReadBackCount:N0} re-parsed, sources {r.SourceTotal:N0}."
                    : $"Verify OK: {r.MergedLineCount:N0} lines, {r.ReadBackCount:N0} re-parsed.");
                Verified = true;

                // Only after a proven-complete merge do we offer to delete the now-redundant sources.
                if (r.SourcesChecked && sources is { Count: > 0 })
                    OfferDelete(sources);
            }
            else
            {
                VerifySummary = "MISMATCH - sources NOT safe to delete. See log.";
                foreach (var issue in r.Issues) AppendLog($"  ! {issue}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Verify failed: {ex.Message}");
            VerifySummary = "Verify failed.";
        }
    }

    // ── Delete-sources prompt ────────────────────────────────────────────────
    // Shown only after a merge whose verify proved nothing was lost.

    [ObservableProperty] private bool _showDeletePrompt;
    [ObservableProperty] private string _deletePromptText = "";
    private List<string> _deletableSources = [];

    private void OfferDelete(IReadOnlyList<string> sources)
    {
        // Never offer to delete the merged output itself, even if a source path collides with it.
        _deletableSources = sources
            .Where(p => !string.Equals(p, OutputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (_deletableSources.Count == 0) return;

        long bytes = 0;
        foreach (var p in _deletableSources)
            try { bytes += new FileInfo(p).Length; } catch { /* ignored */ }
        DeletePromptText =
            $"Merge verified - nothing lost. Delete the {_deletableSources.Count} source file(s) " +
            $"({ToolsInputItem.FormatBytes(bytes)})? The merged file is kept.";
        ShowDeletePrompt = true;
    }

    [RelayCommand]
    private void DeleteSources()
    {
        DeleteDeletableSources();
        ShowDeletePrompt = false;
    }

    [RelayCommand]
    private void KeepSources()
    {
        AppendLog("Kept source files.");
        ShowDeletePrompt = false;
    }

    [RelayCommand]
    private void LoadThenDeleteSources()
    {
        ShowDeletePrompt = false;
        LoadRequested?.Invoke(OutputPath);   // closes this window and loads the merged file
        DeleteDeletableSources();
    }

    private void DeleteDeletableSources()
    {
        int ok = 0;
        foreach (var path in _deletableSources)
        {
            try
            {
                File.Delete(path);
                ok++;
                var item = Inputs.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
                if (item is not null) Inputs.Remove(item);
            }
            catch (Exception ex) { AppendLog($"  ! Delete failed {Path.GetFileName(path)}: {ex.Message}"); }
        }
        AppendLog($"Deleted {ok:N0} of {_deletableSources.Count:N0} source file(s).");
        _deletableSources = [];
    }

    // Load: open the merged (or chosen) file in the main explorer. Available whenever a file
    // exists; the user simply closes the window if they do not want to load.
    private bool CanLoad() => !IsBusy && !string.IsNullOrWhiteSpace(OutputPath) && File.Exists(OutputPath);

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private void Load() => LoadRequested?.Invoke(OutputPath);

    private void AppendLog(string line) =>
        Log = string.IsNullOrEmpty(Log) ? line : $"{Log}\n{line}";

    partial void OnIsBusyChanged(bool value) => NotifyAll();
    partial void OnVerifiedChanged(bool value) { /* step indicator only */ }
    partial void OnOutputPathChanged(string value)
    {
        Verified = false;
        NotifyAll();
    }

    private void NotifyAll()
    {
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        BrowseOutputCommand.NotifyCanExecuteChanged();
        OpenExistingCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        VerifyCommand.NotifyCanExecuteChanged();
        LoadCommand.NotifyCanExecuteChanged();
    }
}
