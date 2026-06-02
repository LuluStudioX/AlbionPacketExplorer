using System.Text.Json;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;
using SukiUI;
using SukiUI.Enums;
using SukiUI.Toasts;

namespace AlbionPacketExplorer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IFilePicker _filePicker;
    private readonly ISukiToastManager _toasts;
    private readonly GameDataService _gameData = new();
    private readonly IconCacheService _iconCache = new();
    private readonly PacketSchemaService _schema = new();
    private readonly RowHideStore _rowHideStore = new();

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
            PacketList.SetResolveItemNames(value);
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

    public bool ForceExpandRows
    {
        get => _packetDetail.ForceExpandRows;
        set
        {
            _packetDetail.ForceExpandRows = value;
            OnPropertyChanged();
            SaveSettings();
        }
    }

    [ObservableProperty] private bool _autoStartCapture;
    [ObservableProperty] private bool _autoSaveLogs;

    partial void OnAutoStartCaptureChanged(bool value) => SaveSettings();
    partial void OnAutoSaveLogsChanged(bool value) => SaveSettings();

    public bool GameDataLoaded => _gameData.IsLoaded;

    [ObservableProperty] private bool _focusMode;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private SukiBackgroundStyle _backgroundStyle = SukiBackgroundStyle.Gradient;

    public ISukiToastManager ToastManager => _toasts;

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
        AppSettingsStore.Save(new AppSettings(ResolveItemNames, ResolveIcons, FocusMode, MinimizeToTray,
            SukiTheme.GetInstance().ActiveBaseTheme == ThemeVariant.Dark, ForceExpandRows,
            AutoStartCapture, AutoSaveLogs));

    public MainViewModel(IFilePicker filePicker, ISukiToastManager toasts)
    {
        _filePicker = filePicker;
        _toasts = toasts;
        _packetDetail = new PacketDetailViewModel(_gameData, _iconCache, _schema, _rowHideStore);
        PacketList.Configure(_gameData, false);
        PacketList.LoadPersistedState();

        var saved = AppSettingsStore.Load();
        _packetDetail.ResolveItemNames = saved.ResolveItemNames;
        PacketList.SetResolveItemNames(saved.ResolveItemNames);
        _packetDetail.ResolveIcons = saved.ResolveIcons;
        _packetDetail.ForceExpandRows = saved.ForceExpandRows;
        FocusMode = saved.FocusMode;
        MinimizeToTray = saved.MinimizeToTray;
        AutoStartCapture = saved.AutoStartCapture;
        AutoSaveLogs = saved.AutoSaveLogs;

        var theme = SukiTheme.GetInstance();
        theme.ChangeBaseTheme(saved.IsDarkMode ? ThemeVariant.Dark : ThemeVariant.Light);
        theme.OnBaseThemeChanged += _ => SaveSettings();

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
        if (_packetDetail.Packet != null)
            _packetDetail.ForceRebuild();
        if (_allPackets.Count > 0 && ResolveItemNames)
            PacketList.SetResolveItemNames(true);
    }

    [RelayCommand]
    public void RefreshDevices()
    {
        try
        {
            AvailableDevices.Clear();
            AvailableDevices.Add(new NetworkDeviceInfo("", "Automatic (all adapters)", -1));
            foreach (var d in NetworkDeviceScanner.GetDevices())
                AvailableDevices.Add(d);

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
            var deviceName = string.IsNullOrEmpty(SelectedDevice?.Name) ? null : SelectedDevice.Name;
            _session.Start(deviceName);
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
        _toasts.CreateToast()
            .WithTitle("Capture Started")
            .WithContent($"Listening on {SelectedDevice?.DisplayName ?? "all devices"}")
            .OfType(NotificationType.Success)
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .Dismiss().ByClicking()
            .Queue();
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
        _toasts.CreateToast()
            .WithTitle("Capture Stopped")
            .WithContent($"{_capturedPackets.Count:N0} packets captured")
            .OfType(NotificationType.Information)
            .Dismiss().After(TimeSpan.FromSeconds(4))
            .Dismiss().ByClicking()
            .Queue();
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
            _toasts.CreateToast()
                .WithTitle("File Loaded")
                .WithContent($"{loaded.Count:N0} packets from {Path.GetFileName(path)}")
                .OfType(NotificationType.Success)
                .Dismiss().After(TimeSpan.FromSeconds(4))
                .Dismiss().ByClicking()
                .Queue();
        }
        catch (Exception ex)
        {
            Aggregator.Reset();
            PacketList.SetSource([]);
            StatusText = $"Error loading file: {ex.Message}";
            _toasts.CreateToast()
                .WithTitle("Load Failed")
                .WithContent(ex.Message)
                .OfType(NotificationType.Error)
                .Dismiss().ByClicking()
                .Queue();
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

    public void TriggerAutoStart()
    {
        if (AutoStartCapture && !IsCapturing && !IsLoading)
            StartCaptureCommand.Execute(null);
    }

    public async Task AutoSaveLogsAsync()
    {
        if (!AutoSaveLogs || _allPackets.Count == 0) return;
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AlbionPacketExplorer", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"packets_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var opts = new JsonSerializerOptions { WriteIndented = false };
            var payload = _allPackets.Select(p => new
            {
                ts = p.Timestamp,
                kind = p.Kind,
                code = p.Code,
                @params = p.Params.ToDictionary(
                    kv => kv.Key,
                    kv => new { type = kv.Value.Type, value = kv.Value.Value })
            });
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65536, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, payload, opts);
        }
        catch { }
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
