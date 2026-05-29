using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

public record ParamRow(string Key, string Type, string Value);

public partial class PacketDetailViewModel : ObservableObject
{
    [ObservableProperty] private PacketEntry? _packet;
    [ObservableProperty] private ObservableCollection<ParamRow> _rows = [];

    partial void OnPacketChanged(PacketEntry? value)
    {
        Rows.Clear();
        if (value == null) return;

        foreach (var (key, pv) in value.Params.OrderBy(p => int.TryParse(p.Key, out var n) ? n : 999))
            Rows.Add(new ParamRow(key, pv.Type, PacketDisplayFormatter.FormatParamValue(pv)));
    }
}
