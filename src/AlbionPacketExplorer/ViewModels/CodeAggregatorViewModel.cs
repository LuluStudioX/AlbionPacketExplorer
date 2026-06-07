using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Services;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

public record CodeStatsRow(CodeStats Stats)
{
    public string Kind => Stats.Kind;
    public int Code => Stats.Code;
    public int Count => Stats.Count;
    public string EventName => PacketNameResolver.Resolve(Stats.Kind, Stats.Code);
    public bool IsKnown => !string.IsNullOrEmpty(EventName);
    public string KeySummary => PacketDisplayFormatter.FormatKeySummary(Stats);
}

public partial class CodeAggregatorViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<CodeStatsRow> _codeStats = [];
    [ObservableProperty] private CodeStatsRow? _selectedRow;

    /// <summary>Per-key field stats for the selected code (drives the field-distribution panel).</summary>
    [ObservableProperty] private ObservableCollection<KeyStats> _selectedKeyStats = [];
    public bool HasSelectedKeyStats => SelectedKeyStats.Count > 0;

    /// <summary>Annotation coverage across all seen codes (how much schema work is left).</summary>
    [ObservableProperty] private string _coverageText = "";

    public CodeStats? SelectedCode => SelectedRow?.Stats;

    private readonly Dictionary<(string Kind, int Code), CodeStats> _map = [];
    private readonly CodeNotesStore _notes = new();

    public CodeAggregatorViewModel() => _notes.Load();

    /// <summary>Editable note for the selected code; persisted on demand via <see cref="SaveNoteCommand"/>.</summary>
    [ObservableProperty] private string _noteDraft = "";

    [RelayCommand]
    private void SaveNote()
    {
        if (SelectedCode is { } c) _notes.Set(c.Kind, c.Code, NoteDraft);
    }

    private string _filterKind = "";
    private string _filterCode = "";
    private string _filterKeys = "";
    private bool _sortUnknownFirst;

    public string FilterKind  { get => _filterKind;  set { _filterKind  = value; OnPropertyChanged(); ApplyFilter(); } }
    public string FilterCode  { get => _filterCode;  set { _filterCode  = value; OnPropertyChanged(); ApplyFilter(); } }
    public string FilterKeys  { get => _filterKeys;  set { _filterKeys  = value; OnPropertyChanged(); ApplyFilter(); } }
    public bool SortUnknownFirst { get => _sortUnknownFirst; set { _sortUnknownFirst = value; OnPropertyChanged(); ApplyFilter(); } }

    public IClipboard? Clipboard { get; set; }
    public ToastService? Toasts { get; set; }
    public PacketSchemaService? Schema { get; set; }

    public void Ingest(PacketEntry packet)
    {
        var stats = GetOrCreateStats(packet.Kind, packet.Code);
        stats.Count++;
        UpdateKeyStats(stats, packet);
    }

    public void Flush()
    {
        ApplyFilter();
        UpdateCoverage();
    }

    // Schema-annotation coverage over every code seen: how many codes resolve to a name and how
    // many byte keys have a curated param name. Surfaces how much annotation work remains.
    private void UpdateCoverage()
    {
        if (_map.Count == 0) { CoverageText = ""; return; }

        var codes = _map.Count;
        var named = _map.Values.Count(s => !string.IsNullOrEmpty(PacketNameResolver.Resolve(s.Kind, s.Code)));
        int totalKeys = 0, annotatedKeys = 0;
        foreach (var s in _map.Values)
            foreach (var k in s.Keys.Values)
            {
                totalKeys++;
                if (!string.IsNullOrEmpty(Schema?.GetParam(s.Kind, s.Code, k.Key)?.Name))
                    annotatedKeys++;
            }

        var codePct = codes == 0 ? 0 : named * 100 / codes;
        var keyPct = totalKeys == 0 ? 0 : annotatedKeys * 100 / totalKeys;
        CoverageText = Loc.Format("summary.coverage", codes.ToString(), codePct.ToString(),
            totalKeys.ToString(), keyPct.ToString());
    }

    private void ApplyFilter()
    {
        var filtered = _map.Values
            .Where(s =>
                FilterHelper.Matches(_filterKind, s.Kind) &&
                FilterHelper.Matches(_filterCode, s.Code.ToString()) &&
                FilterHelper.Matches(_filterKeys, PacketDisplayFormatter.FormatKeySummary(s)));

        IOrderedEnumerable<CodeStats> ordered;
        if (_sortUnknownFirst)
            ordered = filtered
                .OrderBy(s => string.IsNullOrEmpty(PacketNameResolver.Resolve(s.Kind, s.Code)) ? 0 : 1)
                .ThenByDescending(s => s.Count);
        else
            ordered = filtered.OrderByDescending(s => s.Count);

        CodeStats = new ObservableCollection<CodeStatsRow>(ordered.Select(s => new CodeStatsRow(s)));
    }

    public void Reset()
    {
        _map.Clear();
        CodeStats.Clear();
        SelectedRow = null;
        CoverageText = "";
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedCode == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(ConstructorExporter.Export(SelectedCode, Schema));
        Toasts?.Show(Loc.T("toast.copied.keySummary.title"),
            Loc.Format("toast.exportStub.body", SelectedCode.Kind, SelectedCode.Code),
            ToastSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task DiffSatAsync()
    {
        if (SelectedCode is not { } stats || Clipboard == null) return;

        var name = PacketNameResolver.Resolve(stats.Kind, stats.Code);
        if (string.IsNullOrEmpty(name))
        {
            Toasts?.Show(Loc.T("summary.satDiff.title"), Loc.T("summary.satDiff.unknownCode"), ToastSeverity.Warning);
            return;
        }

        var repo = SatConstructorReader.FindRepo();
        if (repo == null)
        {
            Toasts?.Show(Loc.T("summary.satDiff.title"), Loc.T("summary.satDiff.noRepo"), ToastSeverity.Warning);
            return;
        }

        var res = SatConstructorReader.Read(repo, stats.Kind, name);
        if (res == null)
        {
            Toasts?.Show(Loc.T("summary.satDiff.title"), Loc.Format("summary.satDiff.noClass", name), ToastSeverity.Warning);
            return;
        }

        var observed = stats.Keys.Keys
            .Where(k => k != "252" && k != "253")
            .Select(k => int.TryParse(k, out var n) ? n : -1)
            .Where(n => n >= 0)
            .ToHashSet();

        var missing = observed.Where(k => !res.SatReads.Contains(k)).OrderBy(x => x).ToList();
        var extra = res.SatReads.Where(k => !observed.Contains(k)).OrderBy(x => x).ToList();

        var report = new System.Text.StringBuilder();
        report.AppendLine($"// {stats.Kind} {stats.Code} {name} vs SAT");
        report.AppendLine($"// SAT class: {res.ClassName}");
        report.AppendLine($"// {res.FilePath}");
        report.AppendLine($"SAT reads keys:        {string.Join(", ", res.SatReads)}");
        report.AppendLine($"Observed keys:         {string.Join(", ", observed.OrderBy(x => x))}");
        report.AppendLine($"SAT does NOT read:     {(missing.Count == 0 ? "(none)" : string.Join(", ", missing))}   <- candidates to add to SAT");
        report.AppendLine($"SAT reads, not seen:   {(extra.Count == 0 ? "(none)" : string.Join(", ", extra))}");

        await Clipboard.SetTextAsync(report.ToString());
        Toasts?.Show(Loc.T("summary.satDiff.title"),
            Loc.Format("summary.satDiff.done", name, missing.Count == 0 ? "-" : string.Join(",", missing)),
            ToastSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task CopyKeySummaryAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(SelectedRow.KeySummary);
    }

    private bool CanExport() => SelectedCode != null;

    partial void OnSelectedRowChanged(CodeStatsRow? value)
    {
        OnPropertyChanged(nameof(SelectedCode));
        ExportCommand.NotifyCanExecuteChanged();
        CopyKeySummaryCommand.NotifyCanExecuteChanged();
        DiffSatCommand.NotifyCanExecuteChanged();

        var keys = value?.Stats.Keys.Values
            .OrderBy(k => int.TryParse(k.Key, out var n) ? n : int.MaxValue)
            .ToList() ?? [];
        SelectedKeyStats = new ObservableCollection<KeyStats>(keys);
        OnPropertyChanged(nameof(HasSelectedKeyStats));
        NoteDraft = value?.Stats is { } st ? _notes.Get(st.Kind, st.Code) : "";
    }

    private CodeStats GetOrCreateStats(string kind, int code)
    {
        var key = (kind, code);
        if (!_map.TryGetValue(key, out var stats))
        {
            stats = new CodeStats { Kind = kind, Code = code };
            _map[key] = stats;
        }
        return stats;
    }

    private static void UpdateKeyStats(CodeStats stats, PacketEntry packet)
    {
        foreach (var (paramKey, paramVal) in packet.Params)
        {
            if (paramKey == "252" || paramKey == "253") continue;
            var ks = GetOrCreateKeyStats(stats, paramKey);
            ks.PresenceCount++;
            ks.Types.Add(paramVal.Type);
            if (ks.SampleValues.Count < 5)
                ks.SampleValues.Add(paramVal.Value);

            // Value distribution: count distinct stringified values (capped) and track the
            // numeric range so a field's shape (constant / enum-like / id / range) is visible.
            var repr = PacketDisplayFormatter.FormatParamValue(paramVal);
            if (ks.ValueCounts.TryGetValue(repr, out var c))
                ks.ValueCounts[repr] = c + 1;
            else if (ks.ValueCounts.Count < KeyStats.DistinctCap)
                ks.ValueCounts[repr] = 1;
            else
                ks.DistinctCapped = true;

            if (ToDouble(paramVal.Value) is { } d)
            {
                ks.NumericMin = ks.NumericMin is { } mn ? Math.Min(mn, d) : d;
                ks.NumericMax = ks.NumericMax is { } mx ? Math.Max(mx, d) : d;
            }
        }

        foreach (var ks in stats.Keys.Values)
            ks.TotalPackets = stats.Count;
    }

    private static double? ToDouble(object? v) => v switch
    {
        byte b   => b,
        short s  => s,
        int i    => i,
        long l   => l,
        float f  => f,
        double d => d,
        _        => null
    };

    private static KeyStats GetOrCreateKeyStats(CodeStats stats, string paramKey)
    {
        if (!stats.Keys.TryGetValue(paramKey, out var ks))
        {
            ks = new KeyStats { Key = paramKey };
            stats.Keys[paramKey] = ks;
        }
        return ks;
    }
}
