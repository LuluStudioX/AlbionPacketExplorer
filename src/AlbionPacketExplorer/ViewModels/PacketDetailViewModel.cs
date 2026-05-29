using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public PacketDetailViewModel(GameDataService gameData, IconCacheService icons)
    {
        _gameData = gameData;
        _icons = icons;
    }

    partial void OnPacketChanged(PacketEntry? value) => RebuildRows();
    partial void OnResolveItemNamesChanged(bool value) => RebuildRows();

    private void RebuildRows()
    {
        _iconCts.Cancel();
        _iconCts = new CancellationTokenSource();

        Rows.Clear();
        if (Packet == null) return;

        var token = _iconCts.Token;
        var rowsToLoad = new List<ParamRow>();

        foreach (var (key, pv) in Packet.Params.OrderBy(p => int.TryParse(p.Key, out var n) ? n : 999))
        {
            var formatted = PacketDisplayFormatter.FormatParamValue(pv);
            var (resolved, uniqueName) = ResolveItemNames && _gameData.IsLoaded
                ? TryResolveParam(pv)
                : (string.Empty, string.Empty);

            var row = new ParamRow(key, pv.Type, formatted, resolved, uniqueName);
            Rows.Add(row);

            if (ResolveItemNames && !string.IsNullOrEmpty(uniqueName))
                rowsToLoad.Add(row);
        }

        if (rowsToLoad.Count > 0)
            _ = LoadIconsAsync(rowsToLoad, token);
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
