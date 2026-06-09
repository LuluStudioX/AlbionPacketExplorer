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

    /// <summary>Drives the full-window loader overlay (animated GIF). Shown only when a load runs
    /// longer than <see cref="LoaderDelay"/>, then faded out as the packets appear, so quick loads
    /// never flash it.</summary>
    [ObservableProperty] private bool _showLoader;
    private static readonly TimeSpan LoaderDelay = TimeSpan.FromMilliseconds(500);
    private Avalonia.Threading.DispatcherTimer? _loaderDelayTimer;

    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private bool _isPaused;

    /// <summary>Toolbar label/tooltip for the pause-resume toggle, flips with <see cref="IsPaused"/>.</summary>
    public string PauseLabel => Loc.T(IsPaused ? "toolbar.resume" : "toolbar.pause");
    public string PauseTip => Loc.T(IsPaused ? "toolbar.resume.tip" : "toolbar.pause.tip");
    [ObservableProperty] private string? _updateVersion;
    [ObservableProperty] private bool _isUpdating;
    [ObservableProperty] private int _updateProgress;
    [ObservableProperty] private string _updateStatus = "";
    [ObservableProperty] private bool _isCheckingUpdate;

    // _skippedUpdateVersion: skipped forever (persisted) until a newer version or a manual check.
    // _dismissedUpdateVersion: "Not now" hides the badge for this session only (back next launch).
    // _promptedUpdateVersion: already shown this session, so a periodic re-check does not re-pop it.
    // Closing the dialog with X keeps the badge (no version recorded), so it stays one click away.
    private string? _skippedUpdateVersion;
    private string? _dismissedUpdateVersion;
    private string? _promptedUpdateVersion;
    private string? _lastUpdateNotes;

    /// <summary>Raised when an update should be offered: (version, changelog notes). The view opens
    /// the changelog dialog and routes the user's choice back via the methods below.</summary>
    public event Action<string, string?>? UpdateAvailableRequested;
    [ObservableProperty] private string _statusText = Loc.T("status.ready");
    [ObservableProperty] private ObservableCollection<NetworkDeviceInfo> _availableDevices = [];
    [ObservableProperty] private NetworkDeviceInfo? _selectedDevice;

    private CaptureSession? _session;
    // Packed arena holding every packet's raw params-JSON bytes; PacketEntry.Params decodes lazily
    // from it. Replaced fresh on each load/capture (ResetData) so a new dataset never inherits the
    // previous arena's chunks. One store backs the loader, the raw replay and the live session.
    private PackedParamStore _paramStore = new();
    private readonly List<PacketEntry> _capturedPackets = [];
    private readonly List<PacketEntry> _allPackets = [];
    private readonly List<byte[]> _rawPackets = [];   // raw payloads (live capture / loaded .b64) for Save as RAW
    private readonly Lock _rawLock = new();
    private readonly PacketCorrelator _correlator = new();

    private bool HasRaw { get { lock (_rawLock) return _rawPackets.Count > 0; } }

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
            AccentTheme: ThemeService.Instance.ActiveAccent.DisplayName,
            SkippedUpdateVersion: _skippedUpdateVersion));

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
        _skippedUpdateVersion = saved.SkippedUpdateVersion;
        Density = saved.Density;
        SidebarToggleGesture = string.IsNullOrWhiteSpace(saved.SidebarToggleGesture) ? "F5" : saved.SidebarToggleGesture;
        AutoSelectNewestGesture = string.IsNullOrWhiteSpace(saved.AutoSelectNewestGesture) ? "Ctrl+L" : saved.AutoSelectNewestGesture;
        ToggleRowExpandGesture = string.IsNullOrWhiteSpace(saved.ToggleRowExpandGesture) ? "Space" : saved.ToggleRowExpandGesture;
        ShowWelcome = !saved.HasSeenWelcome;

        ThemeService.Instance.Initialize(saved.IsDarkMode, saved.AccentTheme);
        ThemeService.Instance.Changed += SaveSettings;

        _ = LoadGameDataAsync();
        _ = CheckForUpdateAsync();
        StartPeriodicUpdateChecks();
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

    // Re-check periodically so a long-running session notices a new release without a restart.
    // Quiet (same as the startup check); fires on the UI thread via DispatcherTimer.
    private Avalonia.Threading.DispatcherTimer? _updateTimer;

    private void StartPeriodicUpdateChecks()
    {
        _updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.Start();
    }

    // Startup check: quiet. Only reveals the banner if an update exists; stays silent on
    // "up to date" and on errors so launch is not noisy.
    // Velopack's update check throws unless the app is actually installed, so the popup/badge flow
    // cannot be exercised from a dev build. In DEBUG only, setting APX_FAKE_UPDATE=<version> injects a
    // synthetic "update available" so the dialog, badge and dismiss logic can be tested in place.
    private async Task<UpdateService.UpdateCheckResult> CheckUpdaterAsync()
    {
#if DEBUG
        var fake = Environment.GetEnvironmentVariable("APX_FAKE_UPDATE");
        if (!string.IsNullOrWhiteSpace(fake))
            return new UpdateService.UpdateCheckResult(
                fake, null, $"## v{fake}\n\n- Simulated update (APX_FAKE_UPDATE) for testing");
#endif
        return await _updater.CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var r = await CheckUpdaterAsync();
        if (r.NewVersion is { } v)
        {
            // Skipped (persisted) or dismissed-this-session versions stay hidden (no badge, no popup)
            // until a manual check. The auto popup is further rate-limited to once per session by
            // MaybePromptUpdate.
            if (v == _skippedUpdateVersion || v == _dismissedUpdateVersion) return;
            UpdateVersion = v;
            _lastUpdateNotes = r.Notes;
            MaybePromptUpdate(v, r.Notes, manual: false);
        }
    }

    // Offer the update via the changelog dialog. Auto checks respect the skipped version and only
    // prompt once per version per session; a manual check always re-opens it.
    private void MaybePromptUpdate(string version, string? notes, bool manual)
    {
        if (!manual && (version == _skippedUpdateVersion || version == _promptedUpdateVersion)) return;
        _promptedUpdateVersion = version;
        UpdateAvailableRequested?.Invoke(version, notes);
    }

    /// <summary>User chose "skip this version": persist it so auto checks stay quiet, and clear the
    /// toolbar badge. A newer version, or a manual check, surfaces the update again.</summary>
    public void SkipUpdateVersion(string version)
    {
        _skippedUpdateVersion = version;
        UpdateVersion = null;
        SaveSettings();
    }

    /// <summary>User chose "Not now": hide the toolbar badge for this session. A fresh auto-check on
    /// the next launch, or a manual check, surfaces it again.</summary>
    public void DismissUpdateVersion(string version)
    {
        _dismissedUpdateVersion = version;
        UpdateVersion = null;
    }

    // Manual check from the toolbar: verbose, surfaces every outcome (including failures, which
    // the startup check hides) so a misconfigured/offline feed is not a silent black box.
    [RelayCommand(CanExecute = nameof(CanCheckForUpdate))]
    private async Task CheckForUpdate()
    {
        IsCheckingUpdate = true;
        CheckForUpdateCommand.NotifyCanExecuteChanged();
        UpdateStatus = Loc.T("update.checking");
        try
        {
            var r = await CheckUpdaterAsync();
            if (r.Error is { } err)
                UpdateStatus = Loc.Format("update.failed", err);
            else if (r.NewVersion is { } v)
            {
                UpdateVersion = v;
                _lastUpdateNotes = r.Notes;
                UpdateStatus = Loc.Format("update.available", v);
                MaybePromptUpdate(v, r.Notes, manual: true);
            }
            else
                UpdateStatus = Loc.T("update.upToDate");
        }
        finally
        {
            IsCheckingUpdate = false;
            CheckForUpdateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanCheckForUpdate() => !IsCheckingUpdate;

    // Toolbar badge click: re-open the changelog dialog for the known update instead of installing
    // straight away, so the user always sees "what's new" and can choose Update / Not now / Skip.
    [RelayCommand]
    private void ShowUpdate()
    {
        if (UpdateVersion is { } v)
            UpdateAvailableRequested?.Invoke(v, _lastUpdateNotes);
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

        _session = new CaptureSession(_paramStore, OnLivePacketBatch, msg => StatusText = msg, OnRawPacket);

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
        IsPaused = false;
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
        IsPaused = false;
        Aggregator.Flush();
        NotifySaveCommands();
        var count = _capturedPackets.Count.ToString("N0");
        StatusText = Loc.Format("status.captureStopped", count);
        _toasts.Show(Loc.T("toast.captureStopped.title"),
            Loc.Format("toast.captureStopped.body", count),
            ToastSeverity.Info);
    }

    private bool CanStopCapture() => IsCapturing;

    [RelayCommand(CanExecute = nameof(CanStopCapture))]
    private void TogglePause()
    {
        if (_session == null) return;
        IsPaused = !IsPaused;
        _session.IsPaused = IsPaused;

        if (IsPaused)
        {
            Aggregator.Flush();
            NotifySaveCommands();
            StatusText = Loc.Format("status.capturePaused", _capturedPackets.Count.ToString("N0"));
            _toasts.Show(Loc.T("toast.capturePaused.title"), Loc.T("toast.capturePaused.body"), ToastSeverity.Info);
        }
        else
        {
            var deviceLabel = SelectedDevice?.DisplayName ?? Loc.T("status.allDevices");
            StatusText = Loc.Format("status.capturing", deviceLabel);
            _toasts.Show(Loc.T("toast.captureResumed.title"), Loc.T("toast.captureResumed.body"), ToastSeverity.Success);
        }
    }

    private void OnRawPacket(byte[] payload)
    {
        lock (_rawLock) _rawPackets.Add(payload);
    }

    [RelayCommand(CanExecute = nameof(CanOpenFile))]
    private async Task OpenFileAsync()
    {
        var path = await _filePicker.PickOpenFileAsync();
        if (path == null) return;
        if (IsRawFile(path)) await LoadRawAsync(path);
        else await LoadFileAsync(path);
    }

    private static bool IsRawFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".b64" or ".raw";
    }

    private bool CanOpenFile() => !IsLoading && !IsCapturing;

    [RelayCommand(CanExecute = nameof(CanSaveFile))]
    private async Task SaveFileAsync()
    {
        var path = await _filePicker.PickSaveFileAsync($"packets_{DateTime.Now:yyyyMMdd_HHmmss}.json", "json", "JSON");
        if (path == null) return;
        try
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            var payload = _allPackets.Select(PacketWire.ToJsonShape);
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(stream, payload, opts);
            StatusText = Loc.Format("status.saved", _allPackets.Count.ToString("N0"), Path.GetFileName(path));
        }
        catch (Exception ex) { StatusText = Loc.Format("status.saveFailed", ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanSaveFile))]
    private async Task SaveCsvAsync()
    {
        var path = await _filePicker.PickSaveFileAsync($"packets_{DateTime.Now:yyyyMMdd_HHmmss}.csv", "csv", "CSV");
        if (path == null) return;
        try
        {
            await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            await PacketCsvExporter.WriteAsync(stream, _allPackets);
            StatusText = Loc.Format("status.saved", _allPackets.Count.ToString("N0"), Path.GetFileName(path));
        }
        catch (Exception ex) { StatusText = Loc.Format("status.saveFailed", ex.Message); }
    }

    [RelayCommand(CanExecute = nameof(CanSaveRaw))]
    private async Task SaveRawAsync()
    {
        byte[][] raws;
        lock (_rawLock) raws = _rawPackets.ToArray();
        var path = await _filePicker.PickSaveFileAsync($"packets_{DateTime.Now:yyyyMMdd_HHmmss}.b64", "b64", "Raw packets");
        if (path == null) return;
        try
        {
            await using var w = new StreamWriter(path, append: false, System.Text.Encoding.ASCII);
            foreach (var p in raws) await w.WriteLineAsync(Convert.ToBase64String(p));
            StatusText = Loc.Format("status.saved", raws.Length.ToString("N0"), Path.GetFileName(path));
        }
        catch (Exception ex) { StatusText = Loc.Format("status.saveFailed", ex.Message); }
    }

    private bool CanSaveFile() => !IsLoading && _allPackets.Count > 0;
    private bool CanSaveRaw() => !IsLoading && HasRaw;

    /// <summary>Drives the toolbar Save button: enabled when any format is saveable.</summary>
    public bool CanSaveAnything => !IsLoading && (_allPackets.Count > 0 || HasRaw);

    private void NotifySaveCommands()
    {
        SaveFileCommand.NotifyCanExecuteChanged();
        SaveCsvCommand.NotifyCanExecuteChanged();
        SaveRawCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSaveAnything));
    }

    // Replay a raw .b64 capture: decode each payload through the current parser into packets.
    public async Task LoadRawAsync(string path)
    {
        IsLoading = true;
        LoadProgress = 0;
        StatusText = Loc.Format("status.loading", Path.GetFileName(path));
        ResetData();

        var loaded = new List<PacketEntry>();
        var raws = new List<byte[]>();
        try
        {
            // Decode + ingest off the UI thread: aggregator map, correlator and the local lists are
            // not bound, so mutating them here is safe. Only the finalization touches bound state.
            await Task.Run(async () =>
            {
                var parser = new RawAlbionParser(_paramStore);
                parser.PacketReceived += loaded.Add;
                foreach (var line in await File.ReadAllLinesAsync(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    byte[] payload;
                    try { payload = Convert.FromBase64String(line.Trim()); } catch { continue; }
                    raws.Add(payload);
                    try { parser.ReceivePacket(payload); } catch { /* skip undecodable */ }
                }

                foreach (var pe in loaded) { Aggregator.Ingest(pe); _correlator.Observe(pe); }
            });

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Aggregator.Flush();
                lock (_rawLock) _rawPackets.AddRange(raws);
                _allPackets.AddRange(loaded);
                PacketList.SetSource(loaded);
                NotifySaveCommands();
                var fileName = Path.GetFileName(path);
                var count = loaded.Count.ToString("N0");
                StatusText = Loc.Format("status.loaded", count, fileName);
                _toasts.Show(Loc.T("toast.fileLoaded.title"), Loc.Format("toast.fileLoaded.body", count, fileName), ToastSeverity.Success);
            });
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
            CompactAfterLoad(); // release heap grown by transient parse/decode garbage
        }
    }

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
            // Parse + ingest off the UI thread. The reader's Progress<double> was created on the UI
            // thread, so LoadProgress updates still marshal automatically. Aggregator map, correlator
            // and the local list are not bound, so mutating them here is safe.
            await Task.Run(async () =>
            {
                await foreach (var packet in reader.ReadAsync(path, progress, _paramStore))
                {
                    Aggregator.Ingest(packet);
                    _correlator.Observe(packet);
                    loaded.Add(packet);
                }
            });

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                Aggregator.Flush();
                _allPackets.AddRange(loaded);
                PacketList.SetSource(loaded);
                NotifySaveCommands();
                var fileName = Path.GetFileName(path);
                var loadedCount = loaded.Count.ToString("N0");
                StatusText = Loc.Format("status.loaded", loadedCount, fileName);
                _toasts.Show(Loc.T("toast.fileLoaded.title"),
                    Loc.Format("toast.fileLoaded.body", loadedCount, fileName),
                    ToastSeverity.Success);
            });
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
            CompactAfterLoad(); // release heap grown by transient parse garbage
        }
    }

    // Live packets arrive as a batch (CaptureSession buffers them on the capture thread and posts
    // one batch per timer tick on the UI thread), so touching the bound collections here is safe.
    private void OnLivePacketBatch(IReadOnlyList<PacketEntry> batch)
    {
        foreach (var packet in batch)
        {
            _capturedPackets.Add(packet);
            _allPackets.Add(packet);
            Aggregator.Ingest(packet);
            _correlator.Observe(packet);
            // Keep per-packet so PacketList's cached Kind counts stay correct.
            PacketList.AddLivePacket(packet);
        }

        // One flush per batch instead of the old every-100-packets cadence.
        Aggregator.Flush();
    }

    // One-shot compacting collection run once at the very end of a big file load. A multi-million
    // packet load grows the GC heap with transient parse garbage (UTF-8 buffers, JSON tokens) that
    // workstation GC keeps committed but does not return to the OS; this reclaims that slack so
    // committed memory tracks the live retained set. Done OFF the UI thread so it never blocks UI
    // setup, and ONLY here on the load completion path - never during live capture or filtering, and
    // with no periodic/timer GC, so steady-state interactivity is untouched.
    private static void CompactAfterLoad() => Task.Run(() =>
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    });

    private void ResetData()
    {
        _capturedPackets.Clear();
        _allPackets.Clear();
        lock (_rawLock) _rawPackets.Clear();
        _correlator.Reset();
        Aggregator.Reset();
        PacketList.SetSource([]);
        PacketDetail.Packet = null;
        // Drop the previous dataset's param arena entirely. Disposing the old store closes its spill
        // file (DeleteOnClose removes it immediately), so the previous dataset's param bytes leave the
        // process and disk at once; a reload does not stack two datasets' worth of param bytes.
        _paramStore.Dispose();
        _paramStore = new PackedParamStore();
        NotifySaveCommands();
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
        NotifySaveCommands();

        _loaderDelayTimer?.Stop();
        if (value)
        {
            // Only surface the overlay if the load is genuinely slow (> LoaderDelay); fast loads
            // finish before the timer fires and never flash it.
            _loaderDelayTimer = new Avalonia.Threading.DispatcherTimer { Interval = LoaderDelay };
            _loaderDelayTimer.Tick += (_, _) =>
            {
                _loaderDelayTimer?.Stop();
                if (IsLoading) ShowLoader = true;   // still loading after the delay -> fade in
            };
            _loaderDelayTimer.Start();
        }
        else
        {
            ShowLoader = false;   // load done -> fade out, revealing the packets behind
        }
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        StartCaptureCommand.NotifyCanExecuteChanged();
        StopCaptureCommand.NotifyCanExecuteChanged();
        TogglePauseCommand.NotifyCanExecuteChanged();
        SaveFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPausedChanged(bool value)
    {
        OnPropertyChanged(nameof(PauseLabel));
        OnPropertyChanged(nameof(PauseTip));
    }
}
