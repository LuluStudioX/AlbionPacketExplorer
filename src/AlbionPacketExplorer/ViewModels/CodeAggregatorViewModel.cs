using AlbionPacketExplorer.Models;
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
    public string KeySummary => PacketDisplayFormatter.FormatKeySummary(Stats);
}

public partial class CodeAggregatorViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<CodeStatsRow> _codeStats = [];
    [ObservableProperty] private CodeStatsRow? _selectedRow;

    public CodeStats? SelectedCode => SelectedRow?.Stats;

    private readonly Dictionary<(string Kind, int Code), CodeStats> _map = [];

    public IClipboard? Clipboard { get; set; }

    public void Ingest(PacketEntry packet)
    {
        var stats = GetOrCreateStats(packet.Kind, packet.Code);
        stats.Count++;
        UpdateKeyStats(stats, packet);
    }

    public void Flush()
    {
        CodeStats.Clear();
        foreach (var s in _map.Values.OrderByDescending(s => s.Count))
            CodeStats.Add(new CodeStatsRow(s));
    }

    public void Reset()
    {
        _map.Clear();
        CodeStats.Clear();
        SelectedRow = null;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (SelectedCode == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(ConstructorExporter.Export(SelectedCode));
    }

    private bool CanExport() => SelectedCode != null;

    partial void OnSelectedRowChanged(CodeStatsRow? value)
    {
        OnPropertyChanged(nameof(SelectedCode));
        ExportCommand.NotifyCanExecuteChanged();
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
        }

        foreach (var ks in stats.Keys.Values)
            ks.TotalPackets = stats.Count;
    }

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
