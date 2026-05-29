using System.Text.Json;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AlbionPacketExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFilePicker _filePicker;
    private readonly GameDataService _gameData = new();
    private readonly IconCacheService _iconCache = new();
    private readonly PacketSchemaService _schema = new();

    [ObservableProperty] private CodeAggregatorViewModel _aggregator = new();
    [ObservableProperty] private PacketListViewModel _packetList = new();
    private PacketDetailViewModel _packetDetail = null!;
    public PacketDetailViewModel PacketDetail => _packetDetail;
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _statusText = "Select a network device and click Start, or open a file.";
    [ObservableProperty] private ObservableCollection<NetworkDeviceInfo> _availableDevices = [];
    [ObservableProperty] private NetworkDeviceInfo? _selectedDevice;

    private CaptureSession? _session;
    private readonly List<PacketEntry> _capturedPackets = [];
    private readonly List<PacketEntry> _allPackets = [];

    public bool ResolveItemNames
    {
        get => _packetDetail.ResolveItemNames;
        set
        {
            _packetDetail.ResolveItemNames = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool ResolveIcons
    {
        get => _packetDetail.ResolveIcons;
        set
        {
            _packetDetail.ResolveIcons = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    public bool GameDataLoaded => _gameData.IsLoaded;

    [ObservableProperty] private bool _focusMode;
    [ObservableProperty] private bool _minimizeToTray;

    public string LayoutToggleIcon => FocusMode ? "⊞" : "⊟";

    partial void OnFocusModeChanged(bool value)
    {
        OnPropertyChanged(nameof(LayoutToggleIcon));
        SaveSettings();
    }

    partial void OnMinimizeToTrayChanged(bool value) => SaveSettings();

    [RelayCommand]
    private void ToggleFocusMode() => FocusMode = !FocusMode;

    public SettingsViewModel Settings => new(this);

    private void SaveSettings() =>
        AppSettingsStore.Save(new AppSettings(ResolveItemNames, ResolveIcons, FocusMode, MinimizeToTray));

    public MainViewModel(IFilePicker filePicker)
    {
        _filePicker = filePicker;
        _packetDetail = new PacketDetailViewModel(_gameData, _iconCache, _schema);

        var saved = AppSettingsStore.Load();
        _packetDetail.ResolveItemNames = saved.ResolveItemNames;
        _packetDetail.ResolveIcons = saved.ResolveIcons;
        FocusMode = saved.FocusMode;
        MinimizeToTray = saved.MinimizeToTray;

        _ = LoadGameDataAsync();
        RefreshDevices();

        Aggregator.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(CodeAggregatorViewModel.SelectedCode))
                OnAggregatorSelectionChanged();
        };

        PacketList.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(PacketListViewModel.SelectedPacket))
                PacketDetail.Packet = PacketList.SelectedPacket;
        };
    }

    public PacketSchemaService Schema => _schema;

    private async Task LoadGameDataAsync()
    {
        await _schema.LoadAsync();
        await _gameData.LoadAsync(msg => StatusText = msg);
        OnPropertyChanged(nameof(GameDataLoaded));
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        try
        {
            AvailableDevices.Clear();
            foreach (var d in NetworkDeviceScanner.GetDevices())
                AvailableDevices.Add(d);

            if (AvailableDevices.Count > 0)
                SelectedDevice = AvailableDevices[0];
        }
        catch (Exception ex)
        {
            StatusText = $"Could not list devices (Npcap installed?): {ex.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartCapture))]
    private void StartCapture()
    {
        ResetData();

        _session = new CaptureSession(OnLivePacket, msg => StatusText = msg);

        try
        {
            _session.Start(SelectedDevice?.Name);
        }
        catch (Exception ex)
        {
            StatusText = $"Capture failed to start: {ex.Message}";
            _session.Dispose();
            _session = null;
            return;
        }

        IsCapturing = true;
        StatusText = $"Capturing on {SelectedDevice?.DisplayName ?? "all devices"}…";
    }

    private bool CanStartCapture() => !IsCapturing && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanStopCapture))]
    private void StopCapture()
    {
        _session?.Stop();
        _session = null;
        IsCapturing = false;
        Aggregator.Flush();
        StatusText = $"Capture stopped. {_capturedPackets.Count:N0} packets captured.";
    }

    private bool CanStopCapture() => IsCapturing;

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private async Task OpenFileAsync()
    {
        var path = await _filePicker.PickJsonFileAsync();
        if (path == null) return;
        await LoadFileAsync(path);
    }

    private bool CanOpenFile() => !IsLoading && !IsCapturing;

    [RelayCommand(CanExecute = nameof(CanSaveFile))]
    private async Task SaveFileAsync()
    {
        var suggested = $"packets_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var path = await _filePicker.PickSaveJsonFileAsync(suggested);
        if (path == null) return;

        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var payload = _allPackets.Select(p => new
            {
                ts = p.Timestamp,
                kind = p.Kind,
                code = p.Code,
                @params = p.Params.ToDictionary(
                    kv => kv.Key,
                    kv => new { type = kv.Value.Type, value = kv.Value.Value })
            });

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, payload, opts);

            StatusText = $"Saved {_allPackets.Count:N0} packets to {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusText = $"Save failed: {ex.Message}";
        }
    }

    private bool CanSaveFile() => !IsLoading && _allPackets.Count > 0;

    public async Task LoadFileAsync(string path)
    {
        IsLoading = true;
        LoadProgress = 0;
        StatusText = $"Loading {Path.GetFileName(path)}…";
        ResetData();

        var reader = new PacketFileReader();
        var progress = new Progress<double>(p => LoadProgress = p);
        var loaded = new List<PacketEntry>();

        try
        {
            await foreach (var packet in reader.ReadAsync(path, progress))
            {
                Aggregator.Ingest(packet);
                loaded.Add(packet);
            }

            Aggregator.Flush();
            _allPackets.AddRange(loaded);
            PacketList.SetSource(loaded);
            SaveFileCommand.NotifyCanExecuteChanged();
            StatusText = $"Loaded {loaded.Count:N0} packets from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            Aggregator.Reset();
            PacketList.SetSource([]);
            StatusText = $"Error loading file: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            LoadProgress = 1;
        }
    }

    private void OnLivePacket(PacketEntry packet)
    {
        _capturedPackets.Add(packet);
        _allPackets.Add(packet);
        Aggregator.Ingest(packet);
        PacketList.AddLivePacket(packet);

        if (_capturedPackets.Count % 100 == 0)
            Aggregator.Flush();
    }

    private void ResetData()
    {
        _capturedPackets.Clear();
        _allPackets.Clear();
        Aggregator.Reset();
        PacketList.SetSource([]);
        PacketDetail.Packet = null;
    }

    private void OnAggregatorSelectionChanged()
    {
        if (Aggregator.SelectedCode is { } sel)
            PacketList.FilterTo(sel.Kind, sel.Code);
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        StartCaptureCommand.NotifyCanExecuteChanged();
        SaveFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        StartCaptureCommand.NotifyCanExecuteChanged();
        StopCaptureCommand.NotifyCanExecuteChanged();
        SaveFileCommand.NotifyCanExecuteChanged();
    }
}
