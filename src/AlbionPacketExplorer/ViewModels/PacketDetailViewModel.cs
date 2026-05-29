using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

public record ParamRow(string Key, string Type, string Value, string ResolvedName);

public partial class PacketDetailViewModel : ObservableObject
{
    private readonly GameDataService _gameData;

    [ObservableProperty] private PacketEntry? _packet;
    [ObservableProperty] private ObservableCollection<ParamRow> _rows = [];
    [ObservableProperty] private bool _resolveItemNames;

    public PacketDetailViewModel(GameDataService gameData)
    {
        _gameData = gameData;
    }

    partial void OnPacketChanged(PacketEntry? value) => RebuildRows();
    partial void OnResolveItemNamesChanged(bool value) => RebuildRows();

    private void RebuildRows()
    {
        Rows.Clear();
        if (Packet == null) return;

        foreach (var (key, pv) in Packet.Params.OrderBy(p => int.TryParse(p.Key, out var n) ? n : 999))
        {
            var formatted = PacketDisplayFormatter.FormatParamValue(pv);
            var resolved = string.Empty;

            if (ResolveItemNames && _gameData.IsLoaded)
                resolved = TryResolveParam(pv);

            Rows.Add(new ParamRow(key, pv.Type, formatted, resolved));
        }
    }

    private string TryResolveParam(ParamValue pv)
    {
        int? index = pv.Value switch
        {
            long l when l is >= 1 and <= 50000 => (int)l,
            int i when i is >= 1 and <= 50000 => i,
            _ => null
        };

        if (index == null) return string.Empty;
        if (!_gameData.TryResolve(index.Value, out var unique, out var display)) return string.Empty;
        return $"{unique} — {display}";
    }
}
