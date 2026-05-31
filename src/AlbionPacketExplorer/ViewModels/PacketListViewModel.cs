using Avalonia.Controls.Notifications;
using Avalonia.Input.Platform;
using SukiUI.Toasts;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

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

public record PacketFilter(string Kind, string Code, string Name = "", string Params = "")
{
    public static readonly PacketFilter Empty = new("", "");

    public bool Matches(PacketEntry p)
    {
        // compound tokens: -kind:code or kind:code in any field
        if (!string.IsNullOrWhiteSpace(Code) && !MatchesCodeFilter(Code, p.Kind, p.Code)) return false;
        if (!FilterHelper.Matches(Kind, p.Kind)) return false;
        if (!FilterHelper.Matches(Name, PacketNameResolver.Resolve(p.Kind, p.Code))) return false;
        if (!FilterHelper.Matches(Params,
            PacketDisplayFormatter.FormatParamSummary(p),
            p.ResolvedSummary)) return false;
        return true;
    }

    private static bool MatchesCodeFilter(string filter, string kind, int code)
    {
        var codeStr = code.ToString();
        var tokens = filter.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var inclusions = new List<string>();
        var exclusions = new List<string>();
        var compoundExclusions = new List<(string kind, string code)>();
        var compoundInclusions = new List<(string kind, string code)>();

        foreach (var token in tokens)
        {
            if (token.StartsWith('-') && token.Length > 1)
            {
                var term = token[1..];
                var colon = term.IndexOf(':');
                if (colon > 0)
                    compoundExclusions.Add((term[..colon], term[(colon + 1)..]));
                else
                    exclusions.Add(term);
            }
            else
            {
                var colon = token.IndexOf(':');
                if (colon > 0)
                    compoundInclusions.Add((token[..colon], token[(colon + 1)..]));
                else
                    inclusions.Add(token);
            }
        }

        // compound exclusions: -kind:code → reject if both match
        if (compoundExclusions.Any(c =>
                kind.Equals(c.kind, StringComparison.OrdinalIgnoreCase) && c.code == codeStr))
            return false;

        // compound inclusions: kind:code → must match one if any specified
        if (compoundInclusions.Count > 0 &&
            !compoundInclusions.Any(c =>
                kind.Equals(c.kind, StringComparison.OrdinalIgnoreCase) && c.code == codeStr))
            return false;

        // plain exclusions: reject if code matches
        if (exclusions.Any(e => e == codeStr)) return false;

        // plain inclusions: code must match one if any specified
        if (inclusions.Count > 0 && !inclusions.Any(i => i == codeStr)) return false;

        return true;
    }
}

public partial class PacketListViewModel : ObservableObject
{
    private readonly List<PacketEntry> _allPackets = [];
    private PacketFilter _filter = PacketFilter.Empty;

    private GameDataService? _gameData;
    private bool _resolveItemNames;

    [ObservableProperty] private ObservableCollection<FilterPreset> _presets = [];
    [ObservableProperty] private FilterPreset? _selectedPreset;
    [ObservableProperty] private string _newPresetName = "";

    public void LoadPersistedState()
    {
        var last = FilterPresetStore.LoadLastFilter();
        _filter = new PacketFilter(last.Kind, last.Code, last.EventName, last.Params);
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        OnPropertyChanged(nameof(FilterName));
        OnPropertyChanged(nameof(FilterParams));

        Presets = new ObservableCollection<FilterPreset>(FilterPresetStore.LoadPresets());
    }

    private void PersistLastFilter() =>
        FilterPresetStore.SaveLastFilter(new FilterState(_filter.Kind, _filter.Code, _filter.Name, _filter.Params));

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        var name = NewPresetName.Trim();
        var preset = new FilterPreset(name, _filter.Kind, _filter.Code, _filter.Name, _filter.Params);
        var existing = Presets.FirstOrDefault(p => p.Name == name);
        if (existing != null)
            Presets[Presets.IndexOf(existing)] = preset;
        else
            Presets.Add(preset);
        FilterPresetStore.SavePresets(Presets);
        NewPresetName = "";
    }

    private bool CanSavePreset() => !string.IsNullOrWhiteSpace(NewPresetName);

    partial void OnNewPresetNameChanged(string value) =>
        SavePresetCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanLoadPreset))]
    private void LoadPreset()
    {
        if (SelectedPreset == null) return;
        _filter = new PacketFilter(SelectedPreset.Kind, SelectedPreset.Code, SelectedPreset.EventName, SelectedPreset.Params);
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        OnPropertyChanged(nameof(FilterName));
        OnPropertyChanged(nameof(FilterParams));
        PersistLastFilter();
        ApplyFilter();
    }

    private bool CanLoadPreset() => SelectedPreset != null;

    [RelayCommand(CanExecute = nameof(CanLoadPreset))]
    private void DeletePreset()
    {
        if (SelectedPreset == null) return;
        Presets.Remove(SelectedPreset);
        SelectedPreset = null;
        FilterPresetStore.SavePresets(Presets);
    }

    partial void OnSelectedPresetChanged(FilterPreset? value)
    {
        LoadPresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
    }

    public void Configure(GameDataService gameData, bool resolveItemNames)
    {
        _gameData = gameData;
        _resolveItemNames = resolveItemNames;
    }

    public void SetResolveItemNames(bool value)
    {
        _resolveItemNames = value;
        if (value && _gameData?.IsLoaded == true)
            _ = BuildResolvedIndexAsync(_allPackets);
        ApplyFilter();
    }

    [ObservableProperty] private ObservableCollection<PacketRow> _packets = [];
    [ObservableProperty] private bool _autoSelectNewest;

    public event Action<PacketRow>? ScrollToRowRequested;

    private bool _suppressSelectedRowFeedback;
    private PacketRow? _selectedRow;
    public PacketRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (_suppressSelectedRowFeedback && value == null) return;
            if (SetProperty(ref _selectedRow, value))
            {
                OnPropertyChanged(nameof(SelectedPacket));
                CopyParamSummaryCommand.NotifyCanExecuteChanged();
                CopyAsJsonCommand.NotifyCanExecuteChanged();
            }
        }
    }

    [RelayCommand]
    private void ToggleAutoSelectNewest() => AutoSelectNewest = !AutoSelectNewest;

    public PacketEntry? SelectedPacket => SelectedRow?.Packet;
    public IClipboard? Clipboard { get; set; }
    public ISukiToastManager? Toasts { get; set; }

    public string FilterKind
    {
        get => _filter.Kind;
        set { _filter = _filter with { Kind = value }; OnPropertyChanged(); PersistLastFilter(); ApplyFilter(); }
    }

    public string FilterCode
    {
        get => _filter.Code;
        set { _filter = _filter with { Code = value }; OnPropertyChanged(); PersistLastFilter(); ApplyFilter(); }
    }

    public string FilterName
    {
        get => _filter.Name;
        set { _filter = _filter with { Name = value }; OnPropertyChanged(); PersistLastFilter(); ApplyFilter(); }
    }

    public string FilterParams
    {
        get => _filter.Params;
        set { _filter = _filter with { Params = value }; OnPropertyChanged(); PersistLastFilter(); ApplyFilter(); }
    }

    public string CountText => $"{Packets.Count:N0} / {_allPackets.Count:N0} packets";

    public void SetSource(List<PacketEntry> packets)
    {
        _allPackets.Clear();
        _allPackets.AddRange(packets);
        if (_resolveItemNames && _gameData?.IsLoaded == true)
            _ = BuildResolvedIndexAsync(packets);
        ApplyFilter();
    }

    private Task BuildResolvedIndexAsync(IEnumerable<PacketEntry> packets)
    {
        return Task.Run(() =>
        {
            var gd = _gameData;
            if (gd == null) return;
            foreach (var p in packets)
            {
                var parts = new List<string>();
                foreach (var (_, pv) in p.Params)
                {
                    if (pv.Type != "String" || pv.Value is not string s || string.IsNullOrEmpty(s))
                        continue;
                    parts.Add(s);
                    if (gd.TryResolveByUniqueName(s, out var display))
                        parts.Add(display);
                }
                p.ResolvedSummary = string.Join(" ", parts);
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(ApplyFilter);
        });
    }

    public void AddLivePacket(PacketEntry packet)
    {
        _allPackets.Add(packet);
        if (_filter.Matches(packet))
        {
            var row = new PacketRow(packet);
            Packets.Add(row);
            if (AutoSelectNewest)
            {
                SelectedRow = row;
                ScrollToRowRequested?.Invoke(row);
            }
        }
        OnPropertyChanged(nameof(CountText));
    }

    public void FilterTo(string kind, int code)
    {
        _filter = new PacketFilter(kind, code.ToString());
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        OnPropertyChanged(nameof(FilterName));
        OnPropertyChanged(nameof(FilterParams));
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearFilter()
    {
        _filter = PacketFilter.Empty;
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        OnPropertyChanged(nameof(FilterName));
        OnPropertyChanged(nameof(FilterParams));
        ApplyFilter();
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyParamSummaryAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(SelectedRow.ParamSummary);
        Toasts?.CreateToast()
            .WithTitle("Copied")
            .WithContent("Param summary copied to clipboard")
            .OfType(NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(2))
            .Dismiss().ByClicking()
            .Queue();
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyAsJsonAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        var p = SelectedRow.Packet;
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            ts = p.Timestamp,
            kind = p.Kind,
            code = p.Code,
            @params = p.Params.ToDictionary(kv => kv.Key, kv => new { type = kv.Value.Type, value = kv.Value.Value })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await Clipboard.SetTextAsync(json);
        Toasts?.CreateToast()
            .WithTitle("Copied")
            .WithContent("Packet JSON copied to clipboard")
            .OfType(NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(2))
            .Dismiss().ByClicking()
            .Queue();
    }

    private bool CanCopyRow() => SelectedRow != null;

    private void ApplyFilter()
    {
        var rows = _allPackets
            .Where(_filter.Matches)
            .Select(p => new PacketRow(p));

        _suppressSelectedRowFeedback = true;
        Packets = new ObservableCollection<PacketRow>(rows);
        _suppressSelectedRowFeedback = false;

        if (AutoSelectNewest && Packets.Count > 0)
        {
            var target = Packets[^1];
            SelectedRow = target;
            ScrollToRowRequested?.Invoke(target);
        }
        OnPropertyChanged(nameof(CountText));
    }
}
