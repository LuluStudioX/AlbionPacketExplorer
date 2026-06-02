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
    public string DisplayValue => _isExpanded ? ExpandedText : Value;

    private bool _isHidden;
    public bool IsHidden
    {
        get => _isHidden;
        set { _isHidden = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsVisible)); }
    }
    public bool IsVisible => !_isHidden;

    public ParamRow(string key, string schemaName, string type, string value,
                    string resolvedName, string uniqueName, string note, ParamSource source = ParamSource.None)
    {
        Key = key;
        SchemaName = schemaName;
        Type = type;
        Value = value;
        ResolvedName = resolvedName;
        UniqueName = uniqueName;
        Note = note;
        Source = source;
    }
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
    [ObservableProperty] private bool _resolveItemNames;
    [ObservableProperty] private bool _resolveIcons;
    [ObservableProperty] private bool _forceExpandRows;
    [ObservableProperty] private ParamRow? _selectedRow;

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
        RebuildRows();
    }
    public void ForceRebuild() => RebuildRows();
    partial void OnResolveItemNamesChanged(bool value) => RebuildRows();
    partial void OnForceExpandRowsChanged(bool value)
    {
        if (_allRows.Count > 0)
            RebuildRows();
    }
    partial void OnResolveIconsChanged(bool value)
    {
        if (value)
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
                if (resolveAs == "itemIndex" && pv.Type != "Byte[]")
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

            var row = new ParamRow(key, schemaName, pv.Type, formatted, resolved, uniqueName, note, source);
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
                // Arrays: resolve each element as an item index ONLY when the schema
                // explicitly tags the param resolveAs="itemIndex". The global Resolve
                // toggle must NOT blind-resolve raw arrays (e.g. Byte[] GUIDs, zero-padded
                // Int16[]) — that produced garbage item names. Byte[] never index-resolves.
                else if (resolveAs == "itemIndex" && pv.Type != "Byte[]")
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
                // Scalar numerics: only when resolve is on or param has a schema name
                else if (ResolveItemNames || !string.IsNullOrEmpty(schemaName))
                {
                    var idx = ToInt(pv.Value);
                    if (idx != null && _gameData.TryResolve(idx.Value, out var pu, out var pd))
                        row.PreviewText = $"{pu} — {pd}";
                }
            }

            _allRows.Add(row);

            if (ResolveIcons)
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

    private string BuildExpandedText(ParamRow row)
    {
        var raw = row.Value;
        if (raw.StartsWith('[') && raw.EndsWith(']'))
        {
            var inner = raw[1..^1];
            var items = inner.Split(", ");
            var lines = new System.Text.StringBuilder();
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i].Trim();
                string resolved = "";
                if (_gameData.IsLoaded && long.TryParse(item, out var idx) && idx >= 0)
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
        Toasts?.Show("Copied", "Value copied to clipboard", ToastSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyRowAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        var text = string.IsNullOrEmpty(SelectedRow.ResolvedName)
            ? $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}"
            : $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}\t{SelectedRow.ResolvedName}";
        await Clipboard.SetTextAsync(text);
        Toasts?.Show("Copied", "Row copied to clipboard", ToastSeverity.Success);
    }

    [RelayCommand]
    private async Task CopyAllRowsAsync()
    {
        if (Clipboard == null) return;
        var lines = Rows.Select(r => string.IsNullOrEmpty(r.ResolvedName)
            ? $"{r.Key}\t{r.Type}\t{r.Value}"
            : $"{r.Key}\t{r.Type}\t{r.Value}\t{r.ResolvedName}");
        await Clipboard.SetTextAsync(string.Join("\n", lines));
        Toasts?.Show("Copied", "All rows copied to clipboard", ToastSeverity.Success);
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
