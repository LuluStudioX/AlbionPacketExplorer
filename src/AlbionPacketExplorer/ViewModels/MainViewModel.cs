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
    public IFilePicker FilePicker => _filePicker;
    private readonly ToastService _toasts;
    private readonly GameDataService _gameData = new();
    private readonly IconCacheService _iconCache = new();
    private readonly PacketSchemaService _schema = new();
    private readonly RowHideStore _rowHideStore = new();
    private readonly EnumLabelStore _enumLabels = new();
    private readonly UpdateService _updater = new();

    [ObservableProperty] private CodeAggregatorViewModel _aggregator = new();
    [ObservableProperty] private PacketListViewModel _packetList = new();
    private PacketDetailViewModel _packetDetail = null!;
    public PacketDetailViewModel PacketDetail => _packetDetail;
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty] private string _statusText = Loc.T("status.ready");
    [ObservableProperty] private ObservableCollection<NetworkDeviceInfo> _availableDevices = [];
    [ObservableProperty] private NetworkDeviceInfo? _selectedDevice;

    private CaptureSession? _session;
    private readonly List<PacketEntry> _capturedPackets = [];
    private readonly List<PacketEntry> _allPackets = [];
    private readonly PacketCorrelator _correlator = new();

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

    public IconCacheMode IconMode
    {
        get => _packetDetail.IconMode;
        set
        {
            _packetDetail.IconMode = value;
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

    [ObservableProperty] private DetailDensity _density = DetailDensity.Normal;

    /// <summary>Grid row min-height for the active density (Compact/Normal/Comfortable).</summary>
    public double RowHeight => Density switch
    {
        DetailDensity.Compact     => 20,
        DetailDensity.Comfortable => 32,
        _                         => 26
    };

    /// <summary>Grid cell font size for the active density.</summary>
    public double GridFontSize => Density switch
    {
        DetailDensity.Compact     => 11,
        DetailDensity.Comfortable => 13,
        _                         => 12
    };

    /// <summary>Resolved-item icon size for the active density.</summary>
    public double IconSize => Density switch
    {
        DetailDensity.Compact     => 20,
        DetailDensity.Comfortable => 28,
        _                         => 24
    };

    partial void OnDensityChanged(DetailDensity value)
    {
        OnPropertyChanged(nameof(RowHeight));
        OnPropertyChanged(nameof(GridFontSize));
        OnPropertyChanged(nameof(IconSize));
        SaveSettings();
    }

    public bool GameDataLoaded => _gameData.IsLoaded;

    [ObservableProperty] private bool _sidebarVisible = true;
    [ObservableProperty] private bool _minimizeToTray;

    public ToastService ToastManager => _toasts;

    partial void OnMinimizeToTrayChanged(bool value) => SaveSettings();
    partial void OnSidebarVisibleChanged(bool value) => SaveSettings();

    /// <summary>Key gesture (e.g. "F5", "Ctrl+B") that toggles the filter sidebar.</summary>
    [ObservableProperty] private string _sidebarToggleGesture = "F5";

    /// <summary>Key gesture that toggles auto-select-newest on the packet list.</summary>
    [ObservableProperty] private string _autoSelectNewestGesture = "Ctrl+L";

    /// <summary>Key gesture that expands/collapses the selected packet-detail row.</summary>
    [ObservableProperty] private string _toggleRowExpandGesture = "Space";

    partial void OnSidebarToggleGestureChanged(string value)
    {
        SaveSettings();
        ShortcutsChanged?.Invoke();
    }

    partial void OnAutoSelectNewestGestureChanged(string value)
    {
        SaveSettings();
        ShortcutsChanged?.Invoke();
    }

    partial void OnToggleRowExpandGestureChanged(string value)
    {
        SaveSettings();
        ShortcutsChanged?.Invoke();
    }

    /// <summary>Raised when any configurable shortcut changes so the view can rebind them.</summary>
    public event Action? ShortcutsChanged;

    [RelayCommand]
    private void ToggleAutoSelectNewest() =>
        PacketList.AutoSelectNewest = !PacketList.AutoSelectNewest;

    [RelayCommand]
    private void ToggleRowExpand() =>
        PacketDetail.ToggleSelectedRowExpandCommand.Execute(null);

    [RelayCommand]
    private void ToggleSidebar() => SidebarVisible = !SidebarVisible;

    /// <summary>One-time welcome banner shown until the user dismisses it.</summary>
    [ObservableProperty] private bool _showWelcome;

    /// <summary>Available UI language codes (whatever lang/*.json ships or is dropped in).</summary>
    public IReadOnlyList<string> AvailableCultures => LocalizationService.Instance.Available;

    public string Culture
    {
        get => LocalizationService.Instance.CurrentCulture;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == Culture) return;
            LocalizationService.Instance.SetCulture(value);
            OnPropertyChanged();
            SaveSettings();
        }
    }

    [RelayCommand]
    private void DismissWelcome()
    {
        ShowWelcome = false;
        SaveSettings();
    }

    /// <summary>Raised to open Settings on a given section (e.g. from the welcome banner).</summary>
    public event Action<string>? OpenSettingsSectionRequested;

    [RelayCommand]
    private void OpenGlossary() => OpenSettingsSectionRequested?.Invoke("Glossary");

    public SettingsViewModel Settings => new(this);

    private void SaveSettings() =>
        AppSettingsStore.Save(new AppSettings(ResolveItemNames, IconMode, SidebarVisible, MinimizeToTray,
            ThemeService.Instance.IsDark, ForceExpandRows,
            AutoStartCapture, AutoSaveLogs, Density,
            Culture: LocalizationService.Instance.CurrentCulture,
            SidebarToggleGesture: SidebarToggleGesture,
            AutoSelectNewestGesture: AutoSelectNewestGesture,
            ToggleRowExpandGesture: ToggleRowExpandGesture,
            HasSeenWelcome: !ShowWelcome,
            AccentTheme: ThemeService.Instance.ActiveAccent.DisplayName));

    public MainViewModel(IFilePicker filePicker, ToastService toasts)
    {
        _filePicker = filePicker;
        _toasts = toasts;
        _packetDetail = new PacketDetailViewModel(_gameData, _iconCache, _schema, _rowHideStore, _enumLabels);
        _packetDetail.CorrelatedPacketRequested += PacketList.SelectPacket;
        _packetDetail.FollowValueRequested += PacketList.FollowValue;
        Aggregator.Schema = _schema;
        PacketList.Configure(_gameData, false);
        PacketList.LoadPersistedState();

        var saved = AppSettingsStore.Load();
        _packetDetail.ResolveItemNames = saved.ResolveItemNames;
        PacketList.SetResolveItemNames(saved.ResolveItemNames);
        _packetDetail.IconMode = saved.IconMode;
        _packetDetail.ForceExpandRows = saved.ForceExpandRows;
        SidebarVisible = saved.SidebarVisible;
        MinimizeToTray = saved.MinimizeToTray;
        AutoStartCapture = saved.AutoStartCapture;
        AutoSaveLogs = saved.AutoSaveLogs;
        Density = saved.Density;
        SidebarToggleGesture = string.IsNullOrWhiteSpace(saved.SidebarToggleGesture) ? "F5" : saved.SidebarToggleGesture;
        AutoSelectNewestGesture = string.IsNullOrWhiteSpace(saved.AutoSelectNewestGesture) ? "Ctrl+L" : saved.AutoSelectNewestGesture;
        ToggleRowExpandGesture = string.IsNullOrWhiteSpace(saved.ToggleRowExpandGesture) ? "Space" : saved.ToggleRowExpandGesture;
        ShowWelcome = !saved.HasSeenWelcome;

        ThemeService.Instance.Initialize(saved.IsDarkMode, saved.AccentTheme);
        ThemeService.Instance.Changed += SaveSettings;

        _ = LoadGameDataAsync();
        _ = CheckForUpdateAsync();
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
            // Mirror the sidebar status filter into the By-Code list so it shows only the
            // selected kind's codes.
            else if (args.PropertyName == nameof(PacketListViewModel.ActiveStatusFilter))
                Aggregator.FilterKind = PacketList.ActiveStatusFilter == "All"
                    ? string.Empty
                    : PacketList.ActiveStatusFilter;
        };
    }

    public PacketSchemaService Schema => _schema;

    private async Task LoadGameDataAsync()
    {
        await _schema.LoadAsync();
        await _gameData.LoadAsync(msg => StatusText = msg);
        OnPropertyChanged(nameof(GameDataLoaded));
        UpdateDataReadyStatus();
        if (_packetDetail.Packet != null)
            _packetDetail.ForceRebuild();
        if (_allPackets.Count > 0 && ResolveItemNames)
            PacketList.SetResolveItemNames(true);
    }

    // Compose the "data ready" status with item count plus on-disk icon count and size.
    private void UpdateDataReadyStatus()
    {
        if (!_gameData.IsLoaded) return;
        var (iconCount, iconBytes) = IconCacheService.GetDiskStats();
        StatusText = Loc.Format("status.dataReady",
            _gameData.ItemCount.ToString("N0"),
            iconCount.ToString("N0"),
            FormatBytes(iconBytes));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int u = 0;
        while (size >= 1024 && u < units.Length - 1) { size /= 1024; u++; }
        return u == 0 ? $"{size:0} {units[u]}" : $"{size:0.#} {units[u]}";
    }

    private async Task CheckForUpdateAsync()
    {
        var version = await _updater.CheckForUpdateAsync();
        if (version != null)
            UpdateVersion = version;
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        IsUpdating = true;
        var progress = new Progress<int>(p => UpdateProgress = p);
        await _updater.ApplyUpdateAsync(progress);
        IsUpdating = false;
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
            StatusText = Loc.Format("status.devicesFailed", ex.Message);
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
            StatusText = Loc.Format("status.captureFailed", ex.Message);
            _session.Dispose();
            _session = null;
            return;
        }

        IsCapturing = true;
        var deviceLabel = SelectedDevice?.DisplayName ?? Loc.T("status.allDevices");
        StatusText = Loc.Format("status.capturing", deviceLabel);
        _toasts.Show(Loc.T("toast.captureStarted.title"),
            Loc.Format("toast.captureStarted.body", deviceLabel),
            ToastSeverity.Success);
    }

    private bool CanStartCapture() => !IsCapturing && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanStopCapture))]
    private void StopCapture()
    {
        _session?.Stop();
        _session = null;
        IsCapturing = false;
        Aggregator.Flush();
        var count = _capturedPackets.Count.ToString("N0");
        StatusText = Loc.Format("status.captureStopped", count);
        _toasts.Show(Loc.T("toast.captureStopped.title"),
            Loc.Format("toast.captureStopped.body", count),
            ToastSeverity.Info);
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
            var payload = _allPackets.Select(PacketWire.ToJsonShape);

            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, payload, opts);

            StatusText = Loc.Format("status.saved", _allPackets.Count.ToString("N0"), Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusText = Loc.Format("status.saveFailed", ex.Message);
        }
    }

    private bool CanSaveFile() => !IsLoading && _allPackets.Count > 0;

    public async Task LoadFileAsync(string path)
    {
        IsLoading = true;
        LoadProgress = 0;
        StatusText = Loc.Format("status.loading", Path.GetFileName(path));
        ResetData();

        var reader = new PacketFileReader();
        var progress = new Progress<double>(p => LoadProgress = p);
        var loaded = new List<PacketEntry>();

        try
        {
            await foreach (var packet in reader.ReadAsync(path, progress))
            {
                Aggregator.Ingest(packet);
                _correlator.Observe(packet);
                loaded.Add(packet);
            }

            Aggregator.Flush();
            _allPackets.AddRange(loaded);
            PacketList.SetSource(loaded);
            SaveFileCommand.NotifyCanExecuteChanged();
            var fileName = Path.GetFileName(path);
            var loadedCount = loaded.Count.ToString("N0");
            StatusText = Loc.Format("status.loaded", loadedCount, fileName);
            _toasts.Show(Loc.T("toast.fileLoaded.title"),
                Loc.Format("toast.fileLoaded.body", loadedCount, fileName),
                ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            Aggregator.Reset();
            PacketList.SetSource([]);
            StatusText = Loc.Format("status.loadError", ex.Message);
            _toasts.Show(Loc.T("toast.loadFailed.title"), ex.Message, ToastSeverity.Error);
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
        _correlator.Observe(packet);
        PacketList.AddLivePacket(packet);

        if (_capturedPackets.Count % 100 == 0)
            Aggregator.Flush();
    }

    private void ResetData()
    {
        _capturedPackets.Clear();
        _allPackets.Clear();
        _correlator.Reset();
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
            var dir = AppPaths.LogsDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"packets_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            var opts = new JsonSerializerOptions { WriteIndented = false };
            var payload = _allPackets.Select(PacketWire.ToJsonShape);
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
