using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
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
    public string Type { get; }
    public string Value { get; }
    public string ResolvedName { get; }
    public string UniqueName { get; }

    private Bitmap? _icon;
    public Bitmap? Icon
    {
        get => _icon;
        set => SetProperty(ref _icon, value);
    }

    public bool HasResolved => !string.IsNullOrEmpty(ResolvedName);

    public ParamRow(string key, string type, string value, string resolvedName, string uniqueName)
    {
        Key = key;
        Type = type;
        Value = value;
        ResolvedName = resolvedName;
        UniqueName = uniqueName;
    }
}

public partial class PacketDetailViewModel : ObservableObject
{
    private readonly GameDataService _gameData;
    private readonly IconCacheService _icons;
    private CancellationTokenSource _iconCts = new();

    [ObservableProperty] private PacketEntry? _packet;
    [ObservableProperty] private ObservableCollection<ParamRow> _rows = [];
    [ObservableProperty] private bool _resolveItemNames;
    [ObservableProperty] private bool _resolveIcons;
    [ObservableProperty] private ParamRow? _selectedRow;

    public IClipboard? Clipboard { get; set; }

    public PacketDetailViewModel(GameDataService gameData, IconCacheService icons)
    {
        _gameData = gameData;
        _icons = icons;
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

    private void RebuildRows()
    {
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();

        Rows.Clear();
        if (Packet == null) return;

        var rowsToLoad = new List<ParamRow>();

        foreach (var (key, pv) in Packet.Params.OrderBy(p => int.TryParse(p.Key, out var n) ? n : 999))
        {
            var formatted = PacketDisplayFormatter.FormatParamValue(pv);
            var (resolved, uniqueName) = ResolveItemNames && _gameData.IsLoaded
                ? TryResolveParam(pv)
                : (string.Empty, string.Empty);

            var row = new ParamRow(key, pv.Type, formatted, resolved, uniqueName);
            Rows.Add(row);

            if (ResolveIcons && !string.IsNullOrEmpty(uniqueName))
                rowsToLoad.Add(row);
        }

        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, _iconCts.Token);
    }

    private void TriggerIconLoad()
    {
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();

        var rowsToLoad = Rows.Where(r => !string.IsNullOrEmpty(r.UniqueName) && r.Icon == null).ToList();
        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, _iconCts.Token);
    }

    private void ClearIcons()
    {
        _iconCts.Cancel();
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

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyValueAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(SelectedRow.Value);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyRowAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        var text = string.IsNullOrEmpty(SelectedRow.ResolvedName)
            ? $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}"
            : $"{SelectedRow.Key}\t{SelectedRow.Type}\t{SelectedRow.Value}\t{SelectedRow.ResolvedName}";
        await Clipboard.SetTextAsync(text);
    }

    [RelayCommand]
    private async Task CopyAllRowsAsync()
    {
        if (Clipboard == null) return;
        var lines = Rows.Select(r => string.IsNullOrEmpty(r.ResolvedName)
            ? $"{r.Key}\t{r.Type}\t{r.Value}"
            : $"{r.Key}\t{r.Type}\t{r.Value}\t{r.ResolvedName}");
        await Clipboard.SetTextAsync(string.Join("\n", lines));
    }

    private bool CanCopyRow() => SelectedRow != null;

    partial void OnSelectedRowChanged(ParamRow? value)
    {
        CopyValueCommand.NotifyCanExecuteChanged();
        CopyRowCommand.NotifyCanExecuteChanged();
    }

    private (string resolved, string uniqueName) TryResolveParam(ParamValue pv)
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
