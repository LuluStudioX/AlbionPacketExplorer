using AlbionPacketExplorer.Services;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AlbionPacketExplorer.ViewModels;

/// <summary>One instruction line in the capture-permission dialog. <see cref="Command"/> is an
/// optional shell snippet shown in a mono box with a Copy button; null/empty = plain text only.</summary>
public sealed class CaptureHelpStep
{
    public string Text { get; init; } = "";
    public string? Command { get; init; }
    public bool HasCommand => !string.IsNullOrWhiteSpace(Command);
}

/// <summary>
/// Backs the dialog shown when live capture opened no device. Raw packet capture needs elevated
/// privileges (or a driver) on every OS and the app cannot grant them to itself, so this surfaces
/// the exact, platform-specific steps for the running OS and its shipped package formats, plus a
/// one-click action that performs the fix where the OS allows it.
/// </summary>
public partial class CapturePermissionViewModel : ObservableObject
{
    private enum PrimaryAction { None, InstallNpcap, GrantMac, GrantLinux }

    private readonly CaptureSetupService _setup = new();
    private readonly PrimaryAction _action;

    public string Title => Loc.T("capture.help.title");
    public string CopyLabel => Loc.T("capture.help.copy");
    public string CloseLabel => Loc.T("capture.help.close");
    public string OpenGuideLabel => Loc.T("capture.help.openGuide");

    public string Heading { get; }
    public string Intro { get; }
    public IReadOnlyList<CaptureHelpStep> Steps { get; }
    public string GuideUrl { get; }
    public bool HasGuide => !string.IsNullOrWhiteSpace(GuideUrl);

    public string PrimaryActionLabel { get; }
    public bool HasPrimaryAction => _action != PrimaryAction.None;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _actionStatus = "";
    public bool HasActionStatus => !string.IsNullOrEmpty(ActionStatus);
    partial void OnActionStatusChanged(string value) => OnPropertyChanged(nameof(HasActionStatus));
    partial void OnIsBusyChanged(bool value) => RunPrimaryActionCommand.NotifyCanExecuteChanged();

    // Injected by the view so copy uses the real top-level clipboard and the toast goes to the same
    // host as the rest of the app.
    public IClipboard? Clipboard { get; set; }
    public ToastService? Toasts { get; set; }

    public event Action? CloseRequested;
    public event Action<string>? OpenUrlRequested;

    /// <summary>Raised after a one-click grant succeeds so the host can re-scan capture devices.</summary>
    public event Action? AccessGranted;

    public CapturePermissionViewModel()
    {
        if (OperatingSystem.IsWindows())
        {
            Heading = Loc.T("capture.help.win.heading");
            Intro = Loc.T("capture.help.win.intro");
            GuideUrl = CaptureSetupService.NpcapDownloadPage;
            _action = PrimaryAction.InstallNpcap;
            PrimaryActionLabel = Loc.T("capture.help.action.win");
            Steps =
            [
                new CaptureHelpStep { Text = Loc.T("capture.help.win.step.install") },
                new CaptureHelpStep { Text = Loc.T("capture.help.win.step.admin") },
            ];
        }
        else if (OperatingSystem.IsMacOS())
        {
            Heading = Loc.T("capture.help.mac.heading");
            Intro = Loc.T("capture.help.mac.intro");
            GuideUrl = "https://www.wireshark.org/download.html";
            _action = PrimaryAction.GrantMac;
            PrimaryActionLabel = Loc.T("capture.help.action.grant");
            Steps =
            [
                new CaptureHelpStep { Text = Loc.T("capture.help.mac.step.chmodbpf") },
                new CaptureHelpStep
                {
                    Text = Loc.T("capture.help.mac.step.sudo"),
                    Command = "sudo \"/Applications/Albion Packet Explorer.app/Contents/MacOS/AlbionPacketExplorer\"",
                },
                new CaptureHelpStep
                {
                    Text = Loc.T("capture.help.mac.step.perms"),
                    Command = "sudo chmod o+r /dev/bpf*",
                },
            ];
        }
        else
        {
            Heading = Loc.T("capture.help.linux.heading");
            Intro = Loc.T("capture.help.linux.intro");
            GuideUrl = "";
            // setcap cannot persist on the AppImage's FUSE mount, so only offer one-click grant when
            // running from a real on-disk binary; otherwise the manual extract steps below are it.
            _action = CaptureSetupService.IsLinuxAppImage() ? PrimaryAction.None : PrimaryAction.GrantLinux;
            PrimaryActionLabel = Loc.T("capture.help.action.grant");
            Steps =
            [
                new CaptureHelpStep
                {
                    Text = Loc.T("capture.help.linux.step.root"),
                    Command = "sudo ./AlbionPacketExplorer-*.AppImage --appimage-extract-and-run",
                },
                new CaptureHelpStep
                {
                    Text = Loc.T("capture.help.linux.step.setcap"),
                    Command = "./AlbionPacketExplorer-*.AppImage --appimage-extract\n" +
                              "sudo setcap cap_net_raw,cap_net_admin+eip \"$PWD/squashfs-root/AppRun\"\n" +
                              "./squashfs-root/AppRun",
                },
                new CaptureHelpStep
                {
                    Text = Loc.T("capture.help.linux.step.installed"),
                    Command = "sudo setcap cap_net_raw,cap_net_admin+eip \"$(command -v AlbionPacketExplorer)\"",
                },
                new CaptureHelpStep { Text = Loc.T("capture.help.linux.step.libpcap") },
            ];
        }
    }

    [RelayCommand]
    private async Task Copy(string? command)
    {
        if (Clipboard == null || string.IsNullOrWhiteSpace(command)) return;
        await Clipboard.SetTextAsync(command);
        Toasts?.Show(Loc.T("capture.help.copied.title"), Loc.T("capture.help.copied"), ToastSeverity.Success);
    }

    [RelayCommand]
    private void OpenGuide()
    {
        if (HasGuide) OpenUrlRequested?.Invoke(GuideUrl);
    }

    [RelayCommand(CanExecute = nameof(CanRunPrimaryAction))]
    private async Task RunPrimaryAction()
    {
        IsBusy = true;
        try
        {
            switch (_action)
            {
                case PrimaryAction.InstallNpcap:
                    ActionStatus = Loc.T("capture.help.action.busy.fetch");
                    var url = await _setup.GetLatestNpcapInstallerUrlAsync();
                    if (url == null)
                    {
                        OpenUrlRequested?.Invoke(CaptureSetupService.NpcapDownloadPage);
                        ActionStatus = Loc.T("capture.help.action.opened");
                        break;
                    }
                    ActionStatus = Loc.T("capture.help.action.busy.download");
                    var started = await _setup.DownloadAndRunNpcapAsync(url);
                    ActionStatus = Loc.T(started ? "capture.help.action.launched" : "capture.help.action.failed");
                    break;

                case PrimaryAction.GrantMac:
                    ActionStatus = Loc.T("capture.help.action.busy.grant");
                    await GrantThen(_setup.GrantMacBpfAccessAsync());
                    break;

                case PrimaryAction.GrantLinux:
                    ActionStatus = Loc.T("capture.help.action.busy.grant");
                    await GrantThen(_setup.GrantLinuxCaptureAsync());
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task GrantThen(Task<bool> grant)
    {
        var ok = await grant;
        ActionStatus = Loc.T(ok ? "capture.help.action.granted" : "capture.help.action.failed");
        if (ok) AccessGranted?.Invoke();
    }

    private bool CanRunPrimaryAction() => !IsBusy;

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
