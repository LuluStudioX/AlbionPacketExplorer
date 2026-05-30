using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using SukiUI.Toasts;
using Avalonia.Threading;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

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

    private Bitmap? _icon;
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool HasResolved => !string.IsNullOrEmpty(ResolvedName);

    public ParamRow(string key, string schemaName, string type, string value,
                    string resolvedName, string uniqueName, string note)
    {
        Key = key;
        SchemaName = schemaName;
        Type = type;
        Value = value;
        ResolvedName = resolvedName;
        UniqueName = uniqueName;
        Note = note;
    }
}

public partial class PacketDetailViewModel : ObservableObject, IDisposable
{
    private readonly GameDataService _gameData;
    private readonly IconCacheService _icons;
    private readonly PacketSchemaService _schema;
    private CancellationTokenSource _iconCts = new();

    [ObservableProperty] private PacketEntry? _packet;
    [ObservableProperty] private ObservableCollection<ParamRow> _rows = [];
    [ObservableProperty] private bool _resolveItemNames;
    [ObservableProperty] private bool _resolveIcons;
    [ObservableProperty] private ParamRow? _selectedRow;

    public IClipboard? Clipboard { get; set; }
    public ISukiToastManager? Toasts { get; set; }

    public PacketDetailViewModel(GameDataService gameData, IconCacheService icons, PacketSchemaService schema)
    {
        _gameData = gameData;
        _icons = icons;
        _schema = schema;
    }

    partial void OnPacketChanged(PacketEntry? value) => RebuildRows();
    partial void OnResolveItemNamesChanged(bool value) => RebuildRows();
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

            var (resolved, uniqueName) = (string.Empty, string.Empty);
            if (ResolveItemNames && _gameData.IsLoaded)
            {
                // Schema-declared itemIndex resolution (correct) takes priority
                if (resolveAs == "itemIndex")
                    (resolved, uniqueName) = TryResolveByIndex(pv);
                else
                    (resolved, uniqueName) = TryResolveParam(pv);
            }

            var row = new ParamRow(key, schemaName, pv.Type, formatted, resolved, uniqueName, note);
            Rows.Add(row);

            if (ResolveIcons && !string.IsNullOrEmpty(uniqueName))
                rowsToLoad.Add(row);
        }

        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, cts.Token);
    }

    private void TriggerIconLoad()
    {
        var cts = ResetCts();
        var rowsToLoad = Rows.Where(r => !string.IsNullOrEmpty(r.UniqueName) && r.Icon == null).ToList();
        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, cts.Token);
    }

    private void ClearIcons()
    {
        ResetCts();
        foreach (var row in Rows)
            row.Icon = null;
    }

    private async Task LoadIconsAsync(List<ParamRow> rows, CancellationToken token)
    {
        foreach (var row in rows)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                var bitmap = await _icons.GetIconAsync(row.UniqueName);
                if (token.IsCancellationRequested) return;
                await Dispatcher.UIThread.InvokeAsync(() => row.Icon = bitmap);
            }
            catch { }
        }
    }

    public event Action<EditParamViewModel>? EditParamRequested;

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private void EditParam()
    {
        if (SelectedRow == null || Packet == null) return;
        var existing = _schema.GetParam(Packet.Kind, Packet.Code, SelectedRow.Key);
        var vm = new EditParamViewModel(
            _schema,
            Packet.Kind, Packet.Code, SelectedRow.Key,
            existing?.Name ?? string.Empty,
            existing?.Note ?? string.Empty,
            existing?.ResolveAs ?? string.Empty,
            () => RebuildRows());
        EditParamRequested?.Invoke(vm);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyValueAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(SelectedRow.Value);
        Toasts?.CreateToast()
            .WithTitle("Copied")
            .WithContent("Value copied to clipboard")
            .OfType(NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(2))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyRowAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        var text = string.IsNullOrEmpty(SelectedRow.ResolvedName)
            ? $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}"
            : $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}\t{SelectedRow.ResolvedName}";
        await Clipboard.SetTextAsync(text);
        Toasts?.CreateToast()
            .WithTitle("Copied")
            .WithContent("Row copied to clipboard")
            .OfType(NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(2))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand]
    private async Task CopyAllRowsAsync()
    {
        if (Clipboard == null) return;
        var lines = Rows.Select(r => string.IsNullOrEmpty(r.ResolvedName)
            ? $"{r.Key}\t{r.Type}\t{r.Value}"
            : $"{r.Key}\t{r.Type}\t{r.Value}\t{r.ResolvedName}");
        await Clipboard.SetTextAsync(string.Join("\n", lines));
        Toasts?.CreateToast()
            .WithTitle("Copied")
            .WithContent("All rows copied to clipboard")
            .OfType(NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(2))
            .Dismiss().ByClicking()
            .Queue();
    }

    private bool CanCopyRow() => SelectedRow != null;

    partial void OnSelectedRowChanged(ParamRow? value)
    {
        CopyValueCommand.NotifyCanExecuteChanged();
        CopyRowCommand.NotifyCanExecuteChanged();
    }

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

    private (string resolved, string uniqueName) TryResolveByIndex(ParamValue pv)
    {
        int? index = pv.Value switch
        {
            long l when l is >= 1 and <= 50000 => (int)l,
            int i when i is >= 1 and <= 50000 => i,
            _ => null
        };
        if (index == null) return (string.Empty, string.Empty);
        if (!_gameData.TryResolve(index.Value, out var unique, out var display))
            return (string.Empty, string.Empty);
        return ($"{unique} — {display}", unique);
    }
}
