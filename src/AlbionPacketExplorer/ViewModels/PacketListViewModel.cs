using Avalonia.Input.Platform;
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
    public bool IsKnown => !string.IsNullOrEmpty(EventName);
    public int KeyCount => Packet.KeyCount;
    public string ParamSummary => PacketDisplayFormatter.FormatParamSummary(Packet);
}

/// <summary>
/// Compiled filter from a query string. Token syntax:
///   -term          exclude if any column contains term
///   term           include only if any column contains term
///   col:term       scoped inclusion  (col = kind|code|name|params)
///   -col:term      scoped exclusion
/// Exclusions are evaluated before inclusions for short-circuit efficiency.
/// Plain code tokens are matched exactly; all others are substring (case-insensitive).
/// </summary>
public sealed class PacketFilter
{
    public static readonly PacketFilter Empty = new("");

    private readonly struct Token(bool exclude, string? col, string term)
    {
        public readonly bool Exclude = exclude;
        public readonly string? Col = col;   // null = any column
        public readonly string Term = term;
    }

    private readonly Token[] _exclusions;
    private readonly Token[] _inclusions;

    // Whether ANY token can consult the (expensive) name / params columns. If not, Matches skips
    // PacketNameResolver.Resolve / FormatParamSummary entirely. kind/code are always cheap.
    private readonly bool _needsName;
    private readonly bool _needsParams;

    public string Query { get; }

    public bool IsEmpty => _exclusions.Length == 0 && _inclusions.Length == 0;

    public PacketFilter(string? query)
    {
        Query = query ?? string.Empty;
        var excl = new List<Token>();
        var incl = new List<Token>();

        foreach (var raw in Query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool exclude = raw.StartsWith('-') && raw.Length > 1;
            var tok = exclude ? raw[1..] : raw;
            var colon = tok.IndexOf(':');
            string? col = colon > 0 ? tok[..colon].ToLowerInvariant() : null;
            string term = colon > 0 ? tok[(colon + 1)..] : tok;
            if (string.IsNullOrEmpty(term)) continue;
            (exclude ? excl : incl).Add(new Token(exclude, col, term));
        }

        _exclusions = [.. excl];
        _inclusions = [.. incl];

        // A null Col is the generic branch (kind || code || name || params), so it needs both.
        // Otherwise only the matching scoped column is needed.
        foreach (var t in excl.Concat(incl))
        {
            if (t.Col is null or "name") _needsName = true;
            if (t.Col is null or "params") _needsParams = true;
        }
    }

    public bool Matches(PacketEntry p)
    {
        if (_exclusions.Length == 0 && _inclusions.Length == 0) return true;

        var codeStr   = p.Code.ToString();
        var kind      = p.Kind;
        // Only compute the expensive columns if a token can actually consult them. When a flag is
        // false, no token's ColumnMatches branch reads the corresponding arg, so empty is safe.
        var name      = _needsName ? PacketNameResolver.Resolve(p.Kind, p.Code) : string.Empty;
        var paramStr  = _needsParams ? PacketDisplayFormatter.FormatParamSummary(p) : string.Empty;
        var resolved  = _needsParams ? (p.ResolvedSummary ?? string.Empty) : string.Empty;
        var paired    = p.Correlated != null;
        var returnCode = p.ReturnCode;

        // Exclusions first — reject early
        foreach (var t in _exclusions)
        {
            if (ColumnMatches(t.Col, t.Term, kind, codeStr, name, paramStr, resolved, paired, returnCode))
                return false;
        }

        // Inclusions — ALL must match (AND semantics across tokens)
        foreach (var t in _inclusions)
        {
            if (!ColumnMatches(t.Col, t.Term, kind, codeStr, name, paramStr, resolved, paired, returnCode))
                return false;
        }

        return true;
    }

    private static readonly char[] ParamDelimiters = [' ', '=', ',', '[', ']'];

    private static bool ParamMatches(string term, string paramStr, string resolved)
    {
        // Match only value tokens, not key indices.
        // paramStr format: "0=value  1=[2]  2=190,07" — each space-separated chunk is key=value.
        foreach (var pair in paramStr.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var value = pair[(eq + 1)..]; // e.g. "5,5" or "[2]" or "190,07"
            // split value on comma/brackets to get individual numbers
            foreach (var tok in value.Split([',', '[', ']'], StringSplitOptions.RemoveEmptyEntries))
                if (tok.Equals(term, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return resolved.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ColumnMatches(string? col, string term,
        string kind, string codeStr, string name, string paramStr, string resolved,
        bool paired, int? returnCode)
        => col switch
        {
            "kind"   => kind.Contains(term, StringComparison.OrdinalIgnoreCase),
            "code"   => codeStr == term,
            "name"   => name.Contains(term, StringComparison.OrdinalIgnoreCase),
            "params" => ParamMatches(term, paramStr, resolved),
            // paired:yes|no — has a correlated request/response partner.
            "paired" => IsAffirmative(term) ? paired : IsNegative(term) ? !paired : false,
            // failed:yes — response with non-zero ReturnCode; failed:no — a successful response.
            // Both are response-scoped: events/requests (no ReturnCode) match neither.
            "failed" => IsAffirmative(term) ? returnCode is { } rc1 && rc1 != 0
                      : IsNegative(term)    ? returnCode is { } rc0 && rc0 == 0
                      : false,
            // returncode:N — exact Photon ReturnCode match (responses only).
            "returncode" => returnCode?.ToString() == term,
            _        => kind.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || codeStr == term
                     || name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || ParamMatches(term, paramStr, resolved),
        };

    private static bool IsAffirmative(string t) =>
        t is "yes" or "true" or "1" or "y";

    private static bool IsNegative(string t) =>
        t is "no" or "false" or "0" or "n";
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
        Presets = new ObservableCollection<FilterPreset>(FilterPresetStore.LoadPresets());
        _filter = new PacketFilter(last.Query);
        NotifyAllFilterProps();
    }

    private void NotifyAllFilterProps()
    {
        OnPropertyChanged(nameof(FilterQuery));
        OnPropertyChanged(nameof(FilterKind));
        OnPropertyChanged(nameof(FilterCode));
        OnPropertyChanged(nameof(FilterName));
        OnPropertyChanged(nameof(FilterParams));
        OnPropertyChanged(nameof(FilterLabel));
        OnPropertyChanged(nameof(FilterDisplayText));
    }

    private void PersistLastFilter() =>
        FilterPresetStore.SaveLastFilter(new FilterState(_filter.Query));

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        var name = NewPresetName.Trim();
        var preset = new FilterPreset(name, _filter.Query);
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
        _filter = new PacketFilter(SelectedPreset.Query);
        NotifyAllFilterProps();
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
    [ObservableProperty] private bool _sortUnknownFirst;

    public event Action<PacketRow>? ScrollToRowRequested;

    /// <summary>Raised when the user asks to diff two selected packets (left, right in pick order).</summary>
    public event Action<PacketEntry, PacketEntry>? DiffRequested;

    public void RequestDiff(PacketEntry left, PacketEntry right) => DiffRequested?.Invoke(left, right);

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

    partial void OnSortUnknownFirstChanged(bool value) => ApplyFilter();

    public PacketEntry? SelectedPacket => SelectedRow?.Packet;
    public IClipboard? Clipboard { get; set; }
    public ToastService? Toasts { get; set; }

    public string FilterQuery
    {
        get => _filter.Query;
        set
        {
            _filter = new PacketFilter(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(FilterKind));
            OnPropertyChanged(nameof(FilterCode));
            OnPropertyChanged(nameof(FilterName));
            OnPropertyChanged(nameof(FilterParams));
            OnPropertyChanged(nameof(FilterLabel));
            OnPropertyChanged(nameof(FilterDisplayText));
            PersistLastFilter();
            ApplyFilter();
        }
    }

    // Per-column scoped filter helpers — read/write scoped tokens within the unified query.
    // "kind" column: tokens prefixed kind: or plain tokens that are EVENT/REQUEST/RESPONSE
    // "code" column: tokens prefixed code: or plain numeric tokens
    // "name" column: tokens prefixed name:
    // "params" column: tokens prefixed params:
    public string FilterKind
    {
        get => GetScopedTokens("kind");
        set => SetScopedTokens("kind", value);
    }

    public string FilterCode
    {
        get => GetScopedTokens("code");
        set => SetScopedTokens("code", value);
    }

    public string FilterName
    {
        get => GetScopedTokens("name");
        set => SetScopedTokens("name", value);
    }

    public string FilterParams
    {
        get => GetScopedTokens("params");
        set => SetScopedTokens("params", value);
    }

    private string GetScopedTokens(string col)
    {
        var parts = new List<string>();
        foreach (var raw in _filter.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool excl = raw.StartsWith('-') && raw.Length > 1;
            var tok = excl ? raw[1..] : raw;
            var colon = tok.IndexOf(':');
            var tokCol = colon > 0 ? tok[..colon] : null;
            var term = colon > 0 ? tok[(colon + 1)..] : tok;
            if (string.Equals(tokCol, col, StringComparison.OrdinalIgnoreCase))
                parts.Add(excl ? $"-{term}" : term);
        }
        return string.Join(" ", parts);
    }

    private void SetScopedTokens(string col, string value)
    {
        // Remove all existing tokens for this column, then append new ones
        var kept = _filter.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(raw =>
            {
                var tok = raw.TrimStart('-');
                var colon = tok.IndexOf(':');
                var tokCol = colon > 0 ? tok[..colon] : null;
                return !string.Equals(tokCol, col, StringComparison.OrdinalIgnoreCase);
            });

        var added = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.StartsWith('-') ? $"-{col}:{t[1..]}" : $"{col}:{t}");

        var newQuery = string.Join(" ", kept.Concat(added));
        _filter = new PacketFilter(newQuery);
        OnPropertyChanged(nameof(FilterQuery));
        OnPropertyChanged(nameof(FilterLabel));
        OnPropertyChanged(nameof(FilterDisplayText));
        PersistLastFilter();
        ApplyFilter();
    }

    // e.g. "Islands • Custom (code, name)"
    public string FilterLabel
    {
        get
        {
            var q = _filter.Query;
            if (string.IsNullOrWhiteSpace(q)) return string.Empty;

            // check if query exactly matches a saved preset
            var matched = Presets.FirstOrDefault(p => p.Query == q);

            // find which columns have custom (non-preset-base) tokens
            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = tok.TrimStart('-');
                var colon = t.IndexOf(':');
                cols.Add(colon > 0 ? t[..colon] : "any");
            }

            if (matched != null && cols.Count > 0)
                return matched.Name;

            if (matched == null && cols.Count > 0)
            {
                var colList = string.Join(", ", cols.Order());
                return $"Custom ({colList})";
            }

            return string.Empty;
        }
    }

    public string FilterDisplayText => string.IsNullOrWhiteSpace(FilterLabel) ? "Filter…" : FilterLabel;

    public string CountText => $"{Packets.Count:N0} / {_allPackets.Count:N0} packets";

    public void SetSource(List<PacketEntry> packets)
    {
        _allPackets.Clear();
        _allPackets.AddRange(packets);

        // Tally Kind counts in a single pass (empty source => all zeros).
        _countAll = _allPackets.Count;
        _countEvent = 0;
        _countRequest = 0;
        _countResponse = 0;
        foreach (var p in _allPackets)
        {
            switch (p.Kind)
            {
                case "EVENT": _countEvent++; break;
                case "REQUEST": _countRequest++; break;
                case "RESPONSE": _countResponse++; break;
            }
        }

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
        _countAll++;
        switch (packet.Kind)
        {
            case "EVENT": _countEvent++; break;
            case "REQUEST": _countRequest++; break;
            case "RESPONSE": _countResponse++; break;
        }
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
        OnPropertyChanged(nameof(HasData));
    }

    public void FilterTo(string kind, int code)
    {
        _filter = new PacketFilter($"kind:{kind} code:{code}");
        OnPropertyChanged(nameof(FilterQuery));
        ApplyFilter();
    }

    /// <summary>
    /// Filter the list to every packet (any code) that carries <paramref name="value"/> in any
    /// field, in capture order. Used to follow an entity (e.g. an objectId) across packets.
    /// </summary>
    public void FollowValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        FilterQuery = $"params:{value.Trim()}";
    }

    /// <summary>
    /// Select and scroll to a specific packet (used for REQUEST/RESPONSE pair navigation). If the
    /// target is hidden by the active filter, the filter is cleared so it becomes reachable.
    /// </summary>
    public void SelectPacket(PacketEntry packet)
    {
        var row = FindRow(packet);
        if (row == null && !_filter.IsEmpty)
        {
            ClearFilterCommand.Execute(null);
            row = FindRow(packet);
        }
        if (row == null) return;

        SelectedRow = row;
        ScrollToRowRequested?.Invoke(row);
    }

    private PacketRow? FindRow(PacketEntry packet) =>
        Packets.FirstOrDefault(r => ReferenceEquals(r.Packet, packet));

    [RelayCommand]
    private void ClearFilter()
    {
        _filter = PacketFilter.Empty;
        NotifyAllFilterProps();
        PersistLastFilter();
        ApplyFilter();
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyParamSummaryAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        await Clipboard.SetTextAsync(SelectedRow.ParamSummary);
        Toasts?.Show(Loc.T("toast.copied.title"), Loc.T("toast.copied.paramSummary"), ToastSeverity.Success);
    }

    [RelayCommand(CanExecute = nameof(CanCopyRow))]
    private async Task CopyAsJsonAsync()
    {
        if (SelectedRow == null || Clipboard == null) return;
        var p = SelectedRow.Packet;
        var json = System.Text.Json.JsonSerializer.Serialize(PacketWire.ToJsonShape(p),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await Clipboard.SetTextAsync(json);
        Toasts?.Show(Loc.T("toast.copied.title"), Loc.T("toast.copied.packetJson"), ToastSeverity.Success);
    }

    private bool CanCopyRow() => SelectedRow != null;

    // ── Status (Kind) quick-filter, surfaced in the filter sidebar ──────────────
    // Counts are over the full source, independent of the active filter, so the
    // sidebar always shows how many of each kind exist.
    /// <summary>True once any packets are loaded or captured (drives the empty-state overlay).</summary>
    public bool HasData => _allPackets.Count > 0;

    // Cached Kind tallies - maintained in SetSource (one pass) and AddLivePacket (increment),
    // so NotifyStatusCounts no longer triggers three O(n) scans of _allPackets per filter pass.
    private int _countAll;
    private int _countEvent;
    private int _countRequest;
    private int _countResponse;

    public int CountAll      => _countAll;
    public int CountEvent    => _countEvent;
    public int CountRequest  => _countRequest;
    public int CountResponse => _countResponse;

    [ObservableProperty] private string _activeStatusFilter = "All";

    /// <summary>Sets the Kind scoped filter from the sidebar. "All" clears it.</summary>
    public void SetStatusFilter(string status)
    {
        ActiveStatusFilter = status;
        FilterKind = status == "All" ? string.Empty : status;
    }

    [RelayCommand]
    private void FilterStatus(string status) => SetStatusFilter(status);

    private void NotifyStatusCounts()
    {
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(CountAll));
        OnPropertyChanged(nameof(CountEvent));
        OnPropertyChanged(nameof(CountRequest));
        OnPropertyChanged(nameof(CountResponse));
    }

    private void ApplyFilter()
    {
        var filtered = _allPackets.Where(_filter.Matches);

        IEnumerable<PacketEntry> ordered = SortUnknownFirst
            ? filtered.OrderBy(p => string.IsNullOrEmpty(PacketNameResolver.Resolve(p.Kind, p.Code)) ? 0 : 1)
            : filtered;

        var rows = ordered.Select(p => new PacketRow(p));

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
        NotifyStatusCounts();
    }
}
