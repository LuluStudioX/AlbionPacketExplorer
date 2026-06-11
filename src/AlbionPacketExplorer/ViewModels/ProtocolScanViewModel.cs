using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AlbionPacketExplorer.Services;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>One protocol difference, shaped for display.</summary>
public sealed record ProtocolChangeRow(string Symbol, string Title, string Detail);

/// <summary>
/// Drives the Protocol Scanner settings panel: reads the live client's <c>EventCodes</c> /
/// <c>OperationCodes</c>, diffs them against the app's compiled enums, and (opt-in) pings a webhook
/// when a new game patch shifts the codes. Off by default; nothing runs or is sent unless enabled.
/// </summary>
public partial class ProtocolScanViewModel : ObservableObject
{
    private readonly ProtocolScanService _scan;
    private readonly IFilePicker _picker;
    private readonly ToastService _toasts;
    private readonly Action _save;

    // Persisted settings (mirrored into AppSettings via the save callback).
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _scanOnStartup;
    [ObservableProperty] private string _webhookUrl = "";
    [ObservableProperty] private string _clientPathOverride = "";

    // Live UI state.
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _clientVersion;
    [ObservableProperty] private string? _metadataPath;
    [ObservableProperty] private bool _lowConfidence;
    [ObservableProperty] private ObservableCollection<ProtocolChangeRow> _changes = [];

    public bool HasWebhook => !string.IsNullOrWhiteSpace(WebhookUrl);

    public ProtocolScanViewModel(
        ProtocolScanService scan, IFilePicker picker, ToastService toasts, Action save, AppSettings s)
    {
        _scan = scan;
        _picker = picker;
        _toasts = toasts;
        // Seed backing fields directly so initialisation doesn't trigger a settings write.
        _enabled = s.ProtocolScanEnabled;
        _scanOnStartup = s.ProtocolScanOnStartup;
        _webhookUrl = s.ProtocolWebhookUrl ?? "";
        _clientPathOverride = s.AlbionClientPath ?? "";
        _save = save;
    }

    partial void OnEnabledChanged(bool value) => _save();
    partial void OnScanOnStartupChanged(bool value) => _save();
    partial void OnClientPathOverrideChanged(string value) => _save();

    partial void OnWebhookUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasWebhook));
        _save();
    }

    [RelayCommand]
    private Task Scan() => RunScanAsync(notify: true);

    /// <summary>Runs a scan off the UI thread, updates the panel, and (when enabled) notifies once per patch.</summary>
    public async Task RunScanAsync(bool notify)
    {
        if (IsScanning) return;
        IsScanning = true;
        StatusText = "Scanning game client...";
        try
        {
            var path = string.IsNullOrWhiteSpace(ClientPathOverride) ? null : ClientPathOverride.Trim();
            var result = await Task.Run(() => _scan.Scan(path));
            ApplyResult(result);
            if (notify) await MaybeNotifyAsync(result);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ApplyResult(ProtocolScanResult r)
    {
        if (!r.Ok)
        {
            HasResult = false;
            Changes = [];
            StatusText = r.Error ?? "Scan failed.";
            return;
        }

        ClientVersion = r.ClientVersion;
        MetadataPath = r.MetadataPath;
        LowConfidence = r.LowConfidence;
        Changes = new ObservableCollection<ProtocolChangeRow>(r.Changes.Select(ToRow));
        HasResult = true;
        Summary = r.HasChanges
            ? $"New {r.AddedCount} · Shifted {r.ShiftedCount} · Removed {r.RemovedCount}"
            : "App is in sync with this client.";
        StatusText = r.LowConfidence
            ? "Read the client but the layout looks off; treat results as unverified."
            : $"Scanned client {r.ClientVersion ?? "?"}.";
    }

    private static ProtocolChangeRow ToRow(ProtocolChange c) => c.Type switch
    {
        ProtocolChangeType.Added   => new ProtocolChangeRow("+", $"{c.Enum}.{c.Name}", $"= {c.ClientCode}"),
        ProtocolChangeType.Removed => new ProtocolChangeRow("-", $"{c.Enum}.{c.Name}", $"was {c.AppCode}"),
        _                          => new ProtocolChangeRow("~", $"{c.Enum}.{c.Name}", $"{c.AppCode} -> {c.ClientCode}"),
    };

    // Fires the webhook at most once per client revision, and only on real, confident changes.
    private async Task MaybeNotifyAsync(ProtocolScanResult r)
    {
        if (!Enabled || !r.Ok || !r.HasChanges || r.LowConfidence) return;
        if (string.IsNullOrWhiteSpace(WebhookUrl)) return;

        var state = ProtocolScanState.Load();
        if (state.LastNotifiedFingerprint == r.Fingerprint) return;

        var send = await WebhookNotifier.SendChangeAsync(WebhookUrl.Trim(), r);
        if (send.Ok)
        {
            new ProtocolScanState(r.Fingerprint, r.ClientVersion).Save();
            _toasts.Show("Protocol Scanner", $"Notified webhook: {Summary}", ToastSeverity.Success);
        }
        else
        {
            _toasts.Show("Protocol Scanner", $"Webhook failed: {send.Message}", ToastSeverity.Error);
        }
    }

    [RelayCommand]
    private async Task TestWebhook()
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl)) return;
        var send = await WebhookNotifier.SendTestAsync(WebhookUrl.Trim());
        _toasts.Show("Protocol Scanner",
            send.Ok ? "Test sent. Check your channel." : $"Test failed: {send.Message}",
            send.Ok ? ToastSeverity.Success : ToastSeverity.Error);
    }

    [RelayCommand]
    private async Task BrowseClient()
    {
        var folder = await _picker.PickFolderAsync("Choose the Albion Online game folder");
        if (!string.IsNullOrWhiteSpace(folder)) ClientPathOverride = folder;
    }

    [RelayCommand]
    private void ClearClientPath() => ClientPathOverride = "";
}
