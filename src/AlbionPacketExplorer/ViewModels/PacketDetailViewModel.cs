using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using static AlbionPacketExplorer.Services.PacketSchemaService;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

public sealed class ResolvedItem : ObservableObject
{
    public string UniqueName { get; }
    public string DisplayName { get; }

    private Bitmap? _icon;
    public Bitmap? Icon { get => _icon; set => SetProperty(ref _icon, value); }

    public ResolvedItem(string uniqueName, string displayName)
    {
        UniqueName = uniqueName;
        DisplayName = displayName;
    }
}

public sealed class ParamRow : ObservableObject
{
    public string Key { get; }
    public string SchemaName { get; }
    public string KeyDisplay => string.IsNullOrEmpty(SchemaName) ? Key : $"{Key}  {SchemaName}";
    public string Type { get; }
    public string Value { get; }
    public string ResolvedName { get; }
    public string UniqueName { get; }
    public string ResolveAs { get; }
    public string Note { get; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public ParamSource Source { get; }

    public ObservableCollection<ResolvedItem> ResolvedItems { get; } = [];

    private Bitmap? _icon;
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool HasResolved => !string.IsNullOrEmpty(ResolvedName) || ResolvedItems.Count > 0;
    public bool HasResolvedItems => ResolvedItems.Count > 0;
    public bool HasSingleResolved => !string.IsNullOrEmpty(ResolvedName) && ResolvedItems.Count == 0;

    public string PreviewText { get; set; } = string.Empty;
    public bool HasPreview => !string.IsNullOrEmpty(PreviewText);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayValue)); }
    }

    public string ExpandedText { get; set; } = string.Empty;

    // Arrays "[a, b, c]" and dicts "{...}" collapse to a compact "[N items]" / "{N fields}"
    // badge so a long collection never inflates the row to several screens. The full value
    // is one click away (chevron / View Full Value) and via the expanded text.
    public bool IsCollection => IsArray || IsDict;
    private bool IsArray => Value.Length >= 2 && Value[0] == '[' && Value[^1] == ']';
    private bool IsDict => Value.Length >= 2 && Value[0] == '{' && Value[^1] == '}';

    public int CollectionCount
    {
        get
        {
            if (!IsCollection) return 0;
            var inner = Value[1..^1].Trim();
            // Elements are joined with ", " (comma + space). A bare comma is a decimal
            // separator under comma-decimal cultures (e.g. "180,09836"), so split on the
            // ", " delimiter only, never a lone comma.
            return inner.Length == 0 ? 0 : inner.Split(", ").Length;
        }
    }

    public string CollapsedValue =>
        IsArray ? $"[{CollectionCount} items]" :
        IsDict  ? $"{{{CollectionCount} fields}}" :
        Value;

    // What the Value column shows: full text when expanded, collapsed badge for
    // collections, plain value otherwise.
    public string DisplayValue =>
        _isExpanded ? ExpandedText :
        IsCollection ? CollapsedValue :
        Value;

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set { _isHidden = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVisible)); }
    }
    public bool IsVisible => !_isHidden;

    public ParamRow(string key, string schemaName, string type, string value,
                    string resolvedName, string uniqueName, string note,
                    ParamSource source = ParamSource.None, string resolveAs = "")
    {
        Key = key;
        SchemaName = schemaName;
        Type = type;
        Value = value;
        ResolvedName = resolvedName;
        UniqueName = uniqueName;
        ResolveAs = resolveAs;
        Note = note;
        Source = source;
    }
}

/// <summary>
/// One param key compared across a REQUEST/RESPONSE pair. <see cref="Status"/> drives the row
/// tint: "response" (server-added, the fields a response constructor must model) is the payoff.
/// </summary>
public sealed class ParamDiffRow
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public string KeyDisplay => string.IsNullOrEmpty(Name) ? Key : $"{Key}  {Name}";
    public required string RequestValue { get; init; }
    public required string ResponseValue { get; init; }
    public required string Status { get; init; }   // same | changed | request | response

    public bool IsResponseOnly => Status == "response";
    public bool IsRequestOnly  => Status == "request";
    public bool IsChanged      => Status == "changed";
}

public partial class PacketDetailViewModel : ObservableObject, IDisposable
{
    private readonly GameDataService _gameData;
    private readonly IconCacheService _icons;
    private readonly PacketSchemaService _schema;
    private readonly RowHideStore _hideStore;
    private CancellationTokenSource _iconCts = new();

    [ObservableProperty] private PacketEntry? _packet;
    [ObservableProperty] private ObservableCollection<ParamRow> _rows = [];
    [ObservableProperty] private ObservableCollection<ParamDiffRow> _diffRows = [];
    [ObservableProperty] private bool _resolveItemNames;
    [ObservableProperty] private IconCacheMode _iconMode = IconCacheMode.Disk;

    private bool IconsEnabled => IconMode != IconCacheMode.Off;
    [ObservableProperty] private bool _forceExpandRows;
    [ObservableProperty] private ParamRow? _selectedRow;

    /// <summary>True when a packet is selected (drives the detail empty-state overlay).</summary>
    public bool HasPacket => Packet != null;

    // ── Photon response status (RESPONSE packets carry OperationResponse.ReturnCode +
    //    DebugMessage in framing; surfaced here as a banner above the param grid). ──
    public bool HasResponseStatus => Packet?.HasResponseStatus == true;
    public bool ReturnCodeIsError => Packet?.ReturnCode is { } rc && rc != 0;
    public string ReturnCodeText =>
        Packet?.ReturnCode is { } rc ? (rc == 0 ? Loc.Format("detail.returnCode.ok", rc) : rc.ToString()) : string.Empty;
    public bool HasDebugMessage => !string.IsNullOrEmpty(Packet?.DebugMessage);
    public string DebugMessageText => Packet?.DebugMessage ?? string.Empty;

    // ── REQUEST/RESPONSE correlation (pair navigation). ──
    public bool HasCorrelation => Packet?.Correlated != null;

    /// <summary>Label for the paired packet, e.g. "RESPONSE 56  LeaveResponse  ReturnCode 0 (OK)".</summary>
    public string CorrelatedLabel
    {
        get
        {
            if (Packet?.Correlated is not { } c) return string.Empty;
            var name = Network.PacketNameResolver.Resolve(c.Kind, c.Code);
            var label = string.IsNullOrEmpty(name) ? $"{c.Kind} {c.Code}" : $"{c.Kind} {c.Code}  {name}";
            if (c.ReturnCode is { } rc)
                label += $"  {(rc == 0 ? Loc.Format("detail.returnCode.ok", rc) : rc.ToString())}";
            return label;
        }
    }

    /// <summary>True when the banner has anything to show (response status and/or a paired packet).</summary>
    public bool HasBanner => HasResponseStatus || HasCorrelation;

    // ── REQUEST vs RESPONSE field diff (populates the Diff tab when a pair exists). ──
    public bool HasDiff => HasCorrelation;

    /// <summary>Builds the param diff for the current packet's pair, REQUEST on the left.</summary>
    private void BuildDiff()
    {
        if (Packet?.Correlated is not { } partner)
        {
            DiffRows = [];
            return;
        }

        var (request, response) = Packet.Kind == "REQUEST" ? (Packet, partner) : (partner, Packet);

        // Drop the op-code echo key (transport, not payload) so it never shows as a diff row.
        var keys = request.Params.Keys
            .Union(response.Params.Keys)
            .Where(k => k != "253")
            .OrderBy(k => int.TryParse(k, out var n) ? n : int.MaxValue);

        var rows = new List<ParamDiffRow>();
        foreach (var key in keys)
        {
            request.Params.TryGetValue(key, out var a);
            response.Params.TryGetValue(key, out var b);
            var va = a is null ? string.Empty : PacketDisplayFormatter.FormatParamValue(a);
            var vb = b is null ? string.Empty : PacketDisplayFormatter.FormatParamValue(b);
            var status = a is null ? "response"
                       : b is null ? "request"
                       : va == vb  ? "same"
                       :             "changed";
            var name = _schema.GetParam("REQUEST", request.Code, key)?.Name
                     ?? _schema.GetParam("RESPONSE", response.Code, key)?.Name
                     ?? string.Empty;
            rows.Add(new ParamDiffRow
            {
                Key = key, Name = name, RequestValue = va, ResponseValue = vb, Status = status,
            });
        }
        DiffRows = new ObservableCollection<ParamDiffRow>(rows);
    }

    /// <summary>Raised when the user clicks through to the paired REQUEST/RESPONSE packet.</summary>
    public event Action<PacketEntry>? CorrelatedPacketRequested;

    [RelayCommand]
    private void GoToCorrelated()
    {
        if (Packet?.Correlated is { } c)
            CorrelatedPacketRequested?.Invoke(c);
    }

    /// <summary>Rows that resolved to one or more item names (for the Resolved tab).</summary>
    public IEnumerable<ParamRow> ResolvedRows => Rows.Where(r => r.IsVisible && r.HasResolved);
    public bool HasAnyResolved => Rows.Any(r => r.HasResolved);

    /// <summary>The selected packet serialized as indented JSON (for the Raw JSON tab).</summary>
    public string RawJson
    {
        get
        {
            if (Packet is not { } p) return string.Empty;
            return System.Text.Json.JsonSerializer.Serialize(PacketWire.ToJsonShape(p),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
    }

    // Hide preset management
    [ObservableProperty] private ObservableCollection<RowHidePreset> _hidePresets = [];
    [ObservableProperty] private RowHidePreset? _selectedHidePreset;
    [ObservableProperty] private string _newHidePresetName = "";

    private List<ParamRow> _allRows = [];

    private string PacketKey => Packet == null ? "" : $"{Packet.Kind}:{Packet.Code}";

    private string _filterKey = "";
    private string _filterType = "";
    private string _filterValue = "";
    private string _filterResolved = "";

    public string FilterKey
    {
        get => _filterKey;
        set { _filterKey = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string FilterType
    {
        get => _filterType;
        set { _filterType = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string FilterValue
    {
        get => _filterValue;
        set { _filterValue = value; OnPropertyChanged(); ApplyFilter(); }
    }

    public string FilterResolved
    {
        get => _filterResolved;
        set { _filterResolved = value; OnPropertyChanged(); ApplyFilter(); }
    }

    private void ApplyFilter()
    {
        var filtered = _allRows.Where(r =>
            FilterHelper.Matches(_filterKey,      r.Key, r.SchemaName) &&
            FilterHelper.Matches(_filterType,     r.Type) &&
            FilterHelper.Matches(_filterValue,    r.Value) &&
            FilterHelper.Matches(_filterResolved, r.ResolvedName));
        Rows = new ObservableCollection<ParamRow>(filtered);
    }

    public IClipboard? Clipboard { get; set; }
    public ToastService? Toasts { get; set; }

    public PacketDetailViewModel(GameDataService gameData, IconCacheService icons, PacketSchemaService schema, RowHideStore hideStore)
    {
        _gameData = gameData;
        _icons = icons;
        _schema = schema;
        _hideStore = hideStore;
        _hideStore.Load();
        RefreshHidePresets();
    }

    partial void OnPacketChanged(PacketEntry? value)
    {
        foreach (var r in _allRows) r.IsExpanded = false;
        _filterKey = "";
        _filterType = "";
        _filterValue = "";
        _filterResolved = "";
        OnPropertyChanged(nameof(FilterKey));
        OnPropertyChanged(nameof(FilterType));
        OnPropertyChanged(nameof(FilterValue));
        OnPropertyChanged(nameof(FilterResolved));
        OnPropertyChanged(nameof(RawJson));
        OnPropertyChanged(nameof(HasPacket));
        OnPropertyChanged(nameof(HasResponseStatus));
        OnPropertyChanged(nameof(ReturnCodeIsError));
        OnPropertyChanged(nameof(ReturnCodeText));
        OnPropertyChanged(nameof(HasDebugMessage));
        OnPropertyChanged(nameof(DebugMessageText));
        OnPropertyChanged(nameof(HasCorrelation));
        OnPropertyChanged(nameof(CorrelatedLabel));
        OnPropertyChanged(nameof(HasBanner));
        OnPropertyChanged(nameof(HasDiff));
        BuildDiff();
        RebuildRows();
    }

    partial void OnRowsChanged(ObservableCollection<ParamRow> value)
    {
        OnPropertyChanged(nameof(ResolvedRows));
        OnPropertyChanged(nameof(HasAnyResolved));
    }

    public void ForceRebuild() => RebuildRows();
    partial void OnResolveItemNamesChanged(bool value) => RebuildRows();
    partial void OnForceExpandRowsChanged(bool value)
    {
        if (_allRows.Count > 0)
            RebuildRows();
    }
    partial void OnIconModeChanged(IconCacheMode value)
    {
        _icons.Mode = value;
        if (value != IconCacheMode.Off)
            TriggerIconLoad();
        else
            ClearIcons();
    }

    public void Dispose()
    {
        _iconCts.Cancel();
        _iconCts.Dispose();
    }

    private CancellationTokenSource ResetCts()
    {
        _iconCts.Cancel();
        _iconCts.Dispose();
        _iconCts = new CancellationTokenSource();
        return _iconCts;
    }

    private void RebuildRows()
    {
        var cts = ResetCts();

        _allRows.Clear();
        Rows.Clear();
        if (Packet == null) return;

        var rowsToLoad = new List<ParamRow>();

        foreach (var (key, pv) in Packet.Params.OrderBy(p => int.TryParse(p.Key, out var n) ? n : 999))
        {
            var formatted = PacketDisplayFormatter.FormatParamValue(pv);
            var paramSchema = _schema.GetParam(Packet.Kind, Packet.Code, key);
            var schemaName = paramSchema?.Name ?? string.Empty;
            var note = paramSchema?.Note ?? string.Empty;
            var resolveAs = paramSchema?.ResolveAs ?? string.Empty;
            var source = _schema.GetParamSource(Packet.Kind, Packet.Code, key);

            var (resolved, uniqueName) = (string.Empty, string.Empty);
            List<ResolvedItem>? resolvedItems = null;
            if (ResolveItemNames && _gameData.IsLoaded)
            {
                if (resolveAs == "itemIndex" && IsIndexResolvable(pv.Type))
                {
                    var items = ResolveIndexItems(pv);
                    if (items.Count == 1)
                        (resolved, uniqueName) = ($"{items[0].UniqueName} — {items[0].DisplayName}", items[0].UniqueName);
                    else if (items.Count > 1)
                        resolvedItems = items;
                }
                else
                    (resolved, uniqueName) = TryResolveParam(pv);
            }

            var row = new ParamRow(key, schemaName, pv.Type, formatted, resolved, uniqueName, note, source, resolveAs);
            if (resolvedItems != null)
                foreach (var ri in resolvedItems)
                    row.ResolvedItems.Add(ri);

            if (_gameData.IsLoaded)
            {
                // String params: always try to resolve by unique name
                if (pv.Type == "String" && pv.Value is string sv && !string.IsNullOrEmpty(sv))
                {
                    if (_gameData.TryResolveByUniqueName(sv, out var disp))
                    {
                        var item = new ResolvedItem(sv, disp);
                        row.PreviewText = $"{sv} — {disp}";
                        if (row.ResolvedItems.Count == 0 && string.IsNullOrEmpty(row.UniqueName))
                            row.ResolvedItems.Add(item);
                    }
                }
                // Item-index resolution (scalar or array) happens ONLY when the schema
                // explicitly tags the param resolveAs="itemIndex" and the wire type can
                // actually hold an item index (integer, never Single/Double/Byte[]). There
                // is no blind auto-matching: a raw Int/array/Byte[] is never guessed as an
                // item index, because that produced garbage names (GUIDs, positions, etc.).
                else if (resolveAs == "itemIndex" && IsIndexResolvable(pv.Type))
                {
                    var previewItems = ResolveIndexItems(pv);
                    if (previewItems.Count > 0)
                    {
                        row.PreviewText = string.Join("\n", previewItems.Select(i => $"{i.UniqueName} — {i.DisplayName}"));
                        if (row.ResolvedItems.Count == 0 && string.IsNullOrEmpty(row.UniqueName))
                            foreach (var pi in previewItems)
                                row.ResolvedItems.Add(pi);
                    }
                }
            }

            _allRows.Add(row);

            if (IconsEnabled)
            {
                if (!string.IsNullOrEmpty(uniqueName))
                    rowsToLoad.Add(row);
                else if (row.ResolvedItems.Count > 0)
                    rowsToLoad.Add(row);
            }
        }

        ApplyHiddenState();
        ApplyFilter();
        if (ForceExpandRows) ExpandAllRows();

        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, cts.Token);
    }

    private void ApplyHiddenState()
    {
        if (Packet == null) return;
        var hidden = _hideStore.GetHidden(PacketKey);
        foreach (var row in _allRows)
            row.IsHidden = hidden.Contains(row.Key);
    }

    private void ExpandAllRows()
    {
        foreach (var row in _allRows)
        {
            if (ForceExpandRows)
            {
                if (string.IsNullOrEmpty(row.ExpandedText))
                    row.ExpandedText = BuildExpandedText(row);
                row.IsExpanded = true;
            }
            else
            {
                row.IsExpanded = false;
            }
        }
    }

    private void TriggerIconLoad()
    {
        var cts = ResetCts();
        var rowsToLoad = _allRows
            .Where(r => (!string.IsNullOrEmpty(r.UniqueName) && r.Icon == null) ||
                        r.ResolvedItems.Any(ri => ri.Icon == null))
            .ToList();
        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, cts.Token);
    }

    private void ClearIcons()
    {
        ResetCts();
        foreach (var row in _allRows)
        {
            row.Icon = null;
            foreach (var ri in row.ResolvedItems) ri.Icon = null;
        }
    }

    private async Task LoadIconsAsync(List<ParamRow> rows, CancellationToken token)
    {
        foreach (var row in rows)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                if (!string.IsNullOrEmpty(row.UniqueName))
                {
                    var bitmap = await _icons.GetIconAsync(row.UniqueName);
                    if (token.IsCancellationRequested) return;
                    await Dispatcher.UIThread.InvokeAsync(() => row.Icon = bitmap);
                }
                foreach (var ri in row.ResolvedItems)
                {
                    if (token.IsCancellationRequested) return;
                    var bitmap = await _icons.GetIconAsync(ri.UniqueName);
                    if (token.IsCancellationRequested) return;
                    await Dispatcher.UIThread.InvokeAsync(() => ri.Icon = bitmap);
                }
            }
            catch { }
        }
    }

    /// <summary>Raised when the user asks to inspect a row's full value in a popup window.</summary>
    public event Action<ParamRow, GameDataService>? ViewFullValueRequested;

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private void ViewFullValue()
    {
        if (SelectedRow == null) return;
        ViewFullValueRequested?.Invoke(SelectedRow, _gameData);
    }

    [RelayCommand]
    private void ToggleRowExpand(ParamRow? row)
    {
        if (row == null || !row.IsCollection) return;
        if (!row.IsExpanded && string.IsNullOrEmpty(row.ExpandedText))
            row.ExpandedText = BuildExpandedText(row);
        row.IsExpanded = !row.IsExpanded;
    }

    /// <summary>Expand/collapse the currently selected row (Space shortcut in the Params grid).</summary>
    [RelayCommand]
    private void ToggleSelectedRowExpand() => ToggleRowExpand(SelectedRow);

    private string BuildExpandedText(ParamRow row)
    {
        var raw = row.Value;
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            // Only resolve elements as item names when the param is schema-tagged
            // resolveAs="itemIndex" and the wire type is an integer array. Never guess.
            var canResolve = row.ResolveAs == "itemIndex" && IsIndexResolvable(row.Type);
            var inner = raw[1..^1];
            var items = inner.Split(", ");
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i].Trim();
                string resolved = "";
                if (canResolve && _gameData.IsLoaded && long.TryParse(item, out var idx) && idx >= 0)
                    if (_gameData.TryResolve((int)idx, out var u, out var d))
                        resolved = $"  → {u} — {d}";
                lines.AppendLine($"[{i}] {item}{resolved}");
            }
            return lines.ToString().TrimEnd();
        }
        if (raw.StartsWith('{') && raw.EndsWith('}'))
        {
            var inner = raw[1..^1];
            return string.Join("\n", inner.Split(", ").Select(p => $"  {p}"));
        }
        return raw;
    }

    public event Action? PreviewResolveToggled;
    public bool IsPreviewActive { get; private set; }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private void PreviewResolve()
    {
        IsPreviewActive = !IsPreviewActive;
        PreviewResolveToggled?.Invoke();
    }

    public event Action<EditParamViewModel>? EditParamRequested;

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private void EditParam()
    {
        if (SelectedRow == null || Packet == null) return;
        var existing = _schema.GetParam(Packet.Kind, Packet.Code, SelectedRow.Key);
        var src = _schema.GetParamSource(Packet.Kind, Packet.Code, SelectedRow.Key);
        var vm = new EditParamViewModel(
            _schema,
            Packet.Kind, Packet.Code, SelectedRow.Key,
            existing?.Name ?? string.Empty,
            existing?.Note ?? string.Empty,
            existing?.ResolveAs ?? string.Empty,
            () => RebuildRows(),
            src);
        EditParamRequested?.Invoke(vm);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyValueAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(SelectedRow.Value);
        Toasts?.Show(Loc.T("toast.copied.title"), Loc.T("toast.copied.value"), ToastSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyRowAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        var text = string.IsNullOrEmpty(SelectedRow.ResolvedName)
            ? $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}"
            : $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}\t{SelectedRow.ResolvedName}";
        await Clipboard.SetTextAsync(text);
        Toasts?.Show(Loc.T("toast.copied.title"), Loc.T("toast.copied.row"), ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task CopyAllRowsAsync()
    {
        if (Clipboard == null) return;
        var lines = Rows.Select(r => string.IsNullOrEmpty(r.ResolvedName)
            ? $"{r.Key}\t{r.Type}\t{r.Value}"
            : $"{r.Key}\t{r.Type}\t{r.Value}\t{r.ResolvedName}");
        await Clipboard.SetTextAsync(string.Join("\n", lines));
        Toasts?.Show(Loc.T("toast.copied.title"), Loc.T("toast.copied.allRows"), ToastSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private void HideRow()
    {
        if (SelectedRow == null || Packet == null) return;
        _hideStore.Hide(PacketKey, SelectedRow.Key);
        SelectedRow.IsHidden = true;
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private void UnhideRow()
    {
        if (SelectedRow == null || Packet == null) return;
        _hideStore.Unhide(PacketKey, SelectedRow.Key);
        SelectedRow.IsHidden = false;
    }

    [RelayCommand]
    private void UnhideAllRows()
    {
        if (Packet == null) return;
        _hideStore.UnhideAll(PacketKey);
        foreach (var row in _allRows)
            row.IsHidden = false;
    }

    [RelayCommand]
    private void SaveHidePreset()
    {
        if (Packet == null || string.IsNullOrWhiteSpace(NewHidePresetName)) return;
        var hidden = _allRows.Where(r => r.IsHidden).Select(r => r.Key);
        _hideStore.SavePreset(NewHidePresetName.Trim(), PacketKey, hidden);
        NewHidePresetName = "";
        RefreshHidePresets();
    }

    [RelayCommand]
    private void LoadHidePreset()
    {
        if (Packet == null || SelectedHidePreset == null) return;
        _hideStore.ApplyPreset(SelectedHidePreset.Name, PacketKey);
        ApplyHiddenState();
    }

    [RelayCommand]
    private void DeleteHidePreset()
    {
        if (SelectedHidePreset == null) return;
        _hideStore.DeletePreset(SelectedHidePreset.Name);
        SelectedHidePreset = null;
        RefreshHidePresets();
    }

    private void RefreshHidePresets()
    {
        HidePresets = new ObservableCollection<RowHidePreset>(_hideStore.Presets);
    }

    private bool CanCopyRow() => SelectedRow != null;

    partial void OnSelectedRowChanged(ParamRow? value)
    {
        CopyValueCommand.NotifyCanExecuteChanged();
        CopyRowCommand.NotifyCanExecuteChanged();
        PreviewResolveCommand.NotifyCanExecuteChanged();
        ViewFullValueCommand.NotifyCanExecuteChanged();
        EditParamCommand.NotifyCanExecuteChanged();
        HideRowCommand.NotifyCanExecuteChanged();
        UnhideRowCommand.NotifyCanExecuteChanged();
    }

    // Item indices are integers. Float types (Single/Double) hold positions or factors,
    // and Byte[] holds GUIDs/blobs — none are item indices, so they never index-resolve
    // even when a param is mistakenly tagged resolveAs="itemIndex".
    private static bool IsIndexResolvable(string type) => type switch
    {
        "Byte" or "Int16" or "Int32" or "Int64"
            or "Int16[]" or "Int32[]" or "Int64[]" => true,
        _ => false
    };

    private static int? ToInt(object? v) => v switch
    {
        long   l when l is >= 0 and <= int.MaxValue  => (int)l,
        int    i when i >= 0                          => i,
        short  s when s >= 0                          => s,
        byte   b                                      => b,
        double d when d >= 0 && d == Math.Floor(d) && d <= int.MaxValue => (int)d,
        float  f when f >= 0 && f == Math.Floor(f) && f <= int.MaxValue => (int)f,
        _ => null
    };

    private (string resolved, string uniqueName) TryResolveParam(ParamValue pv)
    {
        // String params: value is already a UniqueName (e.g. "T8_LABOURER_HUNTER")
        if (pv.Type == "String" && pv.Value is string s && !string.IsNullOrEmpty(s))
        {
            if (_gameData.TryResolveByUniqueName(s, out var display))
                return ($"{s} — {display}", s);
        }
        return (string.Empty, string.Empty);
    }

    private List<ResolvedItem> ResolveIndexItems(ParamValue pv)
    {
        var results = new List<ResolvedItem>();
        IEnumerable<object?> values = pv.Value switch
        {
            List<object?> list                => list,
            System.Collections.IList arr      => arr.Cast<object?>(),
            _                                 => [pv.Value]
        };

        foreach (var item in values)
        {
            var idx = ToInt(item);
            if (idx == null) continue;
            if (_gameData.TryResolve(idx.Value, out var unique, out var display))
                results.Add(new ResolvedItem(unique, display));
        }
        return results;
    }
}
