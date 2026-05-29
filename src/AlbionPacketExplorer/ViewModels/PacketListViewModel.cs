using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

public record PacketRow(PacketEntry Packet)
{
    public DateTime Timestamp => Packet.Timestamp;
    public string Kind => Packet.Kind;
    public int Code => Packet.Code;
    public string EventName => PacketNameResolver.Resolve(Packet.Kind, Packet.Code);
    public int KeyCount => Packet.KeyCount;
    public string ParamSummary => PacketDisplayFormatter.FormatParamSummary(Packet);
}

public record PacketFilter(string Kind, string Code)
{
    public static readonly PacketFilter Empty = new("", "");

    public bool Matches(PacketEntry p)
    {
        if (!string.IsNullOrWhiteSpace(Kind) && !p.Kind.Equals(Kind, StringComparison.OrdinalIgnoreCase))
            return false;
        if (int.TryParse(Code, out var code) && p.Code != code)
            return false;
        return true;
    }
}

public partial class PacketListViewModel : ObservableObject
{
    private readonly List<PacketEntry> _allPackets = [];
    private PacketFilter _filter = PacketFilter.Empty;

    [ObservableProperty] private ObservableCollection<PacketRow> _packets = [];
    [ObservableProperty] private PacketRow? _selectedRow;

    public PacketEntry? SelectedPacket => SelectedRow?.Packet;

    public string FilterKind
    {
        get => _filter.Kind;
        set { _filter = _filter with { Kind = value }; OnPropertyChanged(); ApplyFilter(); }
    }

    public string FilterCode
    {
        get => _filter.Code;
        set { _filter = _filter with { Code = value }; OnPropertyChanged(); ApplyFilter(); }
    }

    public string CountText => $"{Packets.Count:N0} / {_allPackets.Count:N0} packets";

    public void SetSource(List<PacketEntry> packets)
    {
        _allPackets.Clear();
        _allPackets.AddRange(packets);
        ApplyFilter();
    }

    public void AddLivePacket(PacketEntry packet)
    {
        _allPackets.Add(packet);
        if (_filter.Matches(packet))
            Packets.Add(new PacketRow(packet));
        OnPropertyChanged(nameof(CountText));
    }

    public void FilterTo(string kind, int code)
    {
        _filter = new PacketFilter(kind, code.ToString());
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        _filter = PacketFilter.Empty;
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        ApplyFilter();
    }

    partial void OnSelectedRowChanged(PacketRow? value) => OnPropertyChanged(nameof(SelectedPacket));

    private void ApplyFilter()
    {
        var rows = _allPackets
            .Where(_filter.Matches)
            .Select(p => new PacketRow(p));

        Packets = new ObservableCollection<PacketRow>(rows);
        OnPropertyChanged(nameof(CountText));
    }
}
