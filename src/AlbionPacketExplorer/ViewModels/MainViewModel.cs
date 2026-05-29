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

    [ObservableProperty] private CodeAggregatorViewModel _aggregator = new();
    [ObservableProperty] private PacketListViewModel _packetList = new();
    [ObservableProperty] private PacketDetailViewModel _packetDetail = new();
    [ObservableProperty] private double _loadProgress;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _statusText = "Select a network device and click Start, or open a file.";
    [ObservableProperty] private ObservableCollection<NetworkDeviceInfo> _availableDevices = [];
    [ObservableProperty] private NetworkDeviceInfo? _selectedDevice;

    private CaptureSession? _session;
    private readonly List<PacketEntry> _capturedPackets = [];

    public MainViewModel(IFilePicker filePicker)
    {
        _filePicker = filePicker;
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
            PacketList.SetSource(loaded);
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
        Aggregator.Ingest(packet);
        PacketList.AddLivePacket(packet);

        if (_capturedPackets.Count % 100 == 0)
            Aggregator.Flush();
    }

    private void ResetData()
    {
        _capturedPackets.Clear();
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
    }

    partial void OnIsCapturingChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        StartCaptureCommand.NotifyCanExecuteChanged();
        StopCaptureCommand.NotifyCanExecuteChanged();
    }
}
