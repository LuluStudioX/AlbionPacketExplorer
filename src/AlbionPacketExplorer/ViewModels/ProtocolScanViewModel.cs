using System.Collections.ObjectModel;
using System.Reflection;
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
    private readonly ProtocolOverrideStore _overrides = new();

    // Persisted settings (mirrored into AppSettings via the save callback).
    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _scanOnStartup;
    [ObservableProperty] private string _clientPathOverride = "";

    /// <summary>User-configured notification webhooks (0..n). Blank rows are pruned so they don't bloat.</summary>
    public ObservableCollection<WebhookEntryViewModel> Webhooks { get; } = [];

    // Live UI state.
    private ProtocolScanResult? _lastResult;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isSubmitting;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string? _clientVersion;
    [ObservableProperty] private string? _metadataPath;
    [ObservableProperty] private bool _lowConfidence;
    [ObservableProperty] private ObservableCollection<ProtocolChangeRow> _changes = [];

    public IReadOnlyList<string> WebhookUrls =>
        Webhooks.Select(w => w.Url.Trim()).Where(u => u.Length > 0).Distinct().ToList();

    public bool HasWebhook => WebhookUrls.Count > 0;

    public ProtocolScanViewModel(
        ProtocolScanService scan, IFilePicker picker, ToastService toasts, Action save, AppSettings s)
    {
        _scan = scan;
        _picker = picker;
        _toasts = toasts;
        // Seed backing fields directly so initialisation doesn't trigger a settings write.
        _enabled = s.ProtocolScanEnabled;
        _scanOnStartup = s.ProtocolScanOnStartup;
        _clientPathOverride = s.AlbionClientPath ?? "";
        _save = save;
        foreach (var url in s.EffectiveWebhooks)
            Webhooks.Add(NewEntry(url));
        // Apply any previously-detected code names to the resolver at startup.
        _overrides.Load();
    }

    partial void OnEnabledChanged(bool value) => _save();
    partial void OnScanOnStartupChanged(bool value) => _save();
    partial void OnClientPathOverrideChanged(string value) => _save();

    private WebhookEntryViewModel NewEntry(string url) =>
        new(url, OnWebhooksChanged, RemoveWebhook, TestWebhookEntryAsync);

    private void OnWebhooksChanged()
    {
        OnPropertyChanged(nameof(WebhookUrls));
        OnPropertyChanged(nameof(HasWebhook));
        _save();
    }

    [RelayCommand]
    private void AddWebhook() => Webhooks.Add(NewEntry(""));

    private void RemoveWebhook(WebhookEntryViewModel entry)
    {
        Webhooks.Remove(entry);
        OnWebhooksChanged();
    }

    /// <summary>Drops blank rows so the panel never keeps an empty webhook field around.</summary>
    public void PruneEmptyWebhooks()
    {
        var empties = Webhooks.Where(w => !w.HasUrl).ToList();
        if (empties.Count == 0) return;
        foreach (var e in empties) Webhooks.Remove(e);
        OnWebhooksChanged();
    }

    private async Task TestWebhookEntryAsync(WebhookEntryViewModel entry)
    {
        var url = entry.Url.Trim();
        if (url.Length == 0) return;
        var send = await WebhookNotifier.SendTestAsync(url);
        _toasts.Show("Protocol Scanner",
            send.Ok ? "Test sent. Check your channel." : $"Test failed: {send.Message}",
            send.Ok ? ToastSeverity.Success : ToastSeverity.Error);
    }

    // Manual scan is informational only: it shows the diff but never pings the webhook
    // (notifications come from the silent on-startup scan).
    [RelayCommand]
    private Task Scan() => RunScanAsync(notify: false);

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
            ApplyLocally(result);
            if (notify) await MaybeNotifyAsync(result);
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>True when there is a confident, non-empty diff the user can approve for upload.</summary>
    public bool CanSubmitToCloud =>
        HasResult && !IsSubmitting && _lastResult is { Ok: true, HasChanges: true, LowConfidence: false };

    partial void OnHasResultChanged(bool value) => OnPropertyChanged(nameof(CanSubmitToCloud));
    partial void OnIsSubmittingChanged(bool value) => OnPropertyChanged(nameof(CanSubmitToCloud));

    private void ApplyResult(ProtocolScanResult r)
    {
        _lastResult = r.Ok ? r : null;
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

    // Writes detected code names into the local override so the app labels them right away,
    // without waiting for the next release to bake them into the compiled enums.
    private void ApplyLocally(ProtocolScanResult r)
    {
        if (!Enabled || !r.Ok || r.LowConfidence) return;
        var applied = _overrides.Apply(r.Changes);
        if (applied > 0)
            _toasts.Show("Protocol Scanner",
                $"Updated your local schema for {applied} code change(s). Active now; restart for full effect.",
                ToastSeverity.Success);
    }

    // Fires the webhook at most once per client revision, and only on real, confident changes.
    private async Task MaybeNotifyAsync(ProtocolScanResult r)
    {
        if (!Enabled || !r.Ok || !r.HasChanges || r.LowConfidence) return;
        var urls = WebhookUrls;
        if (urls.Count == 0) return;

        var state = ProtocolScanState.Load();
        if (state.LastNotifiedFingerprint == r.Fingerprint) return;

        var ok = 0;
        foreach (var url in urls)
            if ((await WebhookNotifier.SendChangeAsync(url, r)).Ok) ok++;

        if (ok > 0)
        {
            new ProtocolScanState(r.Fingerprint, r.ClientVersion).Save();
            _toasts.Show("Protocol Scanner",
                $"Notified {ok}/{urls.Count} webhook(s): {Summary}", ToastSeverity.Success);
        }
        else
        {
            _toasts.Show("Protocol Scanner", "All webhooks failed to notify.", ToastSeverity.Error);
        }
    }

    // Pushes the reviewed diff to the project so a maintainer can fold it into the shipped enums.
    // Local -> cloud, so it never runs on its own: the user clicks Submit after seeing the list above.
    [RelayCommand]
    private async Task SubmitToCloud()
    {
        var r = _lastResult;
        if (r is null || !r.Ok || !r.HasChanges || r.LowConfidence) return;
        IsSubmitting = true;
        try
        {
            var json = ProtocolUploadService.BuildPayload(r, AppVersion);
            var res = await ProtocolUploadService.UploadAsync(json);
            _toasts.Show("Protocol Scanner",
                res.Ok ? "Submitted to the project. Thanks for helping keep it current."
                       : $"Submit failed: {res.Message}",
                res.Ok ? ToastSeverity.Success : ToastSeverity.Error);
        }
        finally
        {
            IsSubmitting = false;
        }
    }

    private static string AppVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    [RelayCommand]
    private async Task BrowseClient()
    {
        var folder = await _picker.PickFolderAsync("Choose the Albion Online game folder");
        if (!string.IsNullOrWhiteSpace(folder)) ClientPathOverride = folder;
    }

    [RelayCommand]
    private void ClearClientPath() => ClientPathOverride = "";
}

/// <summary>A single editable webhook row in the Protocol Scanner panel.</summary>
public partial class WebhookEntryViewModel : ObservableObject
{
    private readonly Action _changed;
    private readonly Action<WebhookEntryViewModel> _remove;
    private readonly Func<WebhookEntryViewModel, Task> _test;

    [ObservableProperty] private string _url;
    [ObservableProperty] private bool _isTesting;

    public WebhookEntryViewModel(
        string url, Action changed, Action<WebhookEntryViewModel> remove, Func<WebhookEntryViewModel, Task> test)
    {
        _url = url;
        _changed = changed;
        _remove = remove;
        _test = test;
    }

    public bool HasUrl => !string.IsNullOrWhiteSpace(Url);

    partial void OnUrlChanged(string value)
    {
        OnPropertyChanged(nameof(HasUrl));
        _changed();
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private async Task Test()
    {
        IsTesting = true;
        try { await _test(this); }
        finally { IsTesting = false; }
    }
}
