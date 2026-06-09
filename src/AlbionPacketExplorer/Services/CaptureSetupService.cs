using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// One-click helpers that grant live-capture prerequisites on each OS, so the user does not have to
/// run shell commands by hand. Every platform branch lives here (per the project's cross-platform
/// rule) and every method degrades gracefully: on any failure it returns false/null and the caller
/// falls back to the manual instructions in the capture-permission dialog.
///
/// Windows needs the Npcap driver; macOS and Linux only need capture privileges (BPF read access /
/// CAP_NET_RAW), which the kernel will not let an app grant itself, hence the elevation prompts.
/// </summary>
public sealed class CaptureSetupService
{
    private const string NpcapHome = "https://npcap.com";
    public const string NpcapDownloadPage = "https://npcap.com/#download";

    // Scrape the Npcap homepage for the newest "dist/npcap-<ver>.exe" link and return its absolute
    // URL. Returns null if the page layout changed or the network failed; caller then opens the
    // download page instead.
    public async Task<string?> GetLatestNpcapInstallerUrlAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Add("User-Agent", "AlbionPacketExplorer");
            var html = await http.GetStringAsync(NpcapHome);

            var best = Regex.Matches(html, @"dist/npcap-([0-9]+(?:\.[0-9]+)+)\.exe")
                .Select(m => m.Groups[1].Value)
                .Distinct()
                .Select(v => (Raw: v, Ver: Version.TryParse(v, out var pv) ? pv : null))
                .Where(x => x.Ver != null)
                .OrderByDescending(x => x.Ver)
                .Select(x => x.Raw)
                .FirstOrDefault();

            return best == null ? null : $"{NpcapHome}/dist/npcap-{best}.exe";
        }
        catch
        {
            return null;
        }
    }

    // Download the Npcap installer to a temp file and launch it. The installer itself prompts for
    // elevation (UAC), so we start it normally with shell execute. Returns true once it is running;
    // bundling/silent install is not possible with the free Npcap (that needs an OEM license).
    public async Task<bool> DownloadAndRunNpcapAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.Add("User-Agent", "AlbionPacketExplorer");
            var bytes = await http.GetByteArrayAsync(url);

            var path = Path.Combine(Path.GetTempPath(), "npcap-installer.exe");
            await File.WriteAllBytesAsync(path, bytes);

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch
        {
            return false;
        }
    }

    // macOS: open read access to the BPF devices for this boot via an authenticated chmod. ChmodBPF
    // (shipped with Wireshark) is the persistent equivalent; this is the no-extra-install path.
    public async Task<bool> GrantMacBpfAccessAsync()
    {
        var psi = new ProcessStartInfo("osascript")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-e");
        // BPF devices are opened O_RDWR by libpcap, so read alone is not enough; grant o+rw.
        psi.ArgumentList.Add("do shell script \"chmod o+rw /dev/bpf*\" with administrator privileges");
        return await RunAndCheckAsync(psi);
    }

    // macOS persistent: install a root LaunchDaemon (the same idea as Wireshark's ChmodBPF) that
    // re-opens BPF access at every boot, so capture works without a prompt after a reboot. One admin
    // authorization installs the script + plist and loads it (and runs it once for the current boot).
    public async Task<bool> InstallMacBpfDaemonAsync()
    {
        try
        {
            const string label = "dk.lulustudio.apx.ChmodBPF";
            const string supportDir = "/Library/Application Support/AlbionPacketExplorer";
            const string scriptDest = supportDir + "/ChmodBPF";
            const string plistDest = "/Library/LaunchDaemons/" + label + ".plist";

            var tmpScript = Path.Combine(Path.GetTempPath(), "apx-chmodbpf.sh");
            var tmpPlist = Path.Combine(Path.GetTempPath(), "apx-chmodbpf.plist");

            await File.WriteAllTextAsync(tmpScript, "#!/bin/sh\nchmod o+rw /dev/bpf*\n");
            await File.WriteAllTextAsync(tmpPlist,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\"><dict>" +
                "<key>Label</key><string>" + label + "</string>" +
                "<key>RunAtLoad</key><true/>" +
                "<key>ProgramArguments</key><array><string>" + scriptDest + "</string></array>" +
                "</dict></plist>\n");

            // All privileged moves in one authenticated shell call. Single-quoted paths only, joined
            // with && so it stays a single AppleScript string.
            var cmd = string.Join(" && ",
                $"mkdir -p '{supportDir}'",
                $"cp '{tmpScript}' '{scriptDest}'",
                $"chown root:wheel '{scriptDest}'",
                $"chmod 755 '{scriptDest}'",
                $"cp '{tmpPlist}' '{plistDest}'",
                $"chown root:wheel '{plistDest}'",
                $"chmod 644 '{plistDest}'",
                $"launchctl load -w '{plistDest}'",
                $"'{scriptDest}'");

            var psi = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"do shell script \"{cmd}\" with administrator privileges");
            return await RunAndCheckAsync(psi);
        }
        catch
        {
            return false;
        }
    }

    // Linux: grant CAP_NET_RAW/CAP_NET_ADMIN to the running binary via pkexec (GUI auth). Only valid
    // when we can resolve a real on-disk executable; an AppImage runs from a FUSE mount where file
    // capabilities do not persist, so that case returns false and the caller shows the manual steps.
    public async Task<bool> GrantLinuxCaptureAsync()
    {
        var binary = ResolveLinuxBinary();
        if (binary == null) return false;

        var psi = new ProcessStartInfo("pkexec")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("setcap");
        psi.ArgumentList.Add("cap_net_raw,cap_net_admin+eip");
        psi.ArgumentList.Add(binary);
        return await RunAndCheckAsync(psi);
    }

    // Linux quick path: re-exec the AppImage as root via pkexec, forwarding the X session so the GUI
    // still draws, then the caller shuts the unprivileged instance down. Returns false if there is no
    // AppImage to relaunch, or if pkexec exits with an error (cancelled auth) within the grace window
    // so the caller does NOT close the app and leave the user with nothing.
    public async Task<bool> RelaunchLinuxAsRootAsync()
    {
        try
        {
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrEmpty(appImage)) return false;

            var psi = new ProcessStartInfo("pkexec") { UseShellExecute = false };
            psi.ArgumentList.Add("env");
            var display = Environment.GetEnvironmentVariable("DISPLAY");
            var xauth = Environment.GetEnvironmentVariable("XAUTHORITY");
            if (!string.IsNullOrEmpty(display)) psi.ArgumentList.Add($"DISPLAY={display}");
            if (!string.IsNullOrEmpty(xauth)) psi.ArgumentList.Add($"XAUTHORITY={xauth}");
            psi.ArgumentList.Add(appImage);
            psi.ArgumentList.Add("--appimage-extract-and-run");

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            // pkexec stays alive as the parent of the (long-running) root app; if the user cancels
            // the auth dialog it exits quickly with a non-zero code. Wait briefly to tell the two
            // apart before the caller decides whether to quit.
            await Task.Delay(2500);
            return !(proc.HasExited && proc.ExitCode != 0);
        }
        catch
        {
            return false;
        }
    }

    // Linux persistent no-sudo path. AppImages run from a nosuid FUSE mount where file capabilities
    // are ignored, so extract the payload to a stable on-disk location, setcap the real apphost there
    // (one pkexec auth), and add a menu entry pointing at it. A non-AppImage build (dev/installed) can
    // setcap its running binary directly. Returns false on any failure; caller shows the manual steps.
    public async Task<bool> SetupLinuxNoSudoCaptureAsync()
    {
        try
        {
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrEmpty(appImage))
                return await GrantLinuxCaptureAsync();   // already on disk: just setcap it

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var libDir = Path.Combine(home, ".local", "lib");
            var targetDir = Path.Combine(libDir, "AlbionPacketExplorer");
            Directory.CreateDirectory(libDir);

            // --appimage-extract always writes ./squashfs-root in the working dir. Extract straight
            // into ~/.local/lib (same filesystem) so the rename to targetDir cannot cross devices.
            var extracted = Path.Combine(libDir, "squashfs-root");
            if (Directory.Exists(extracted)) Directory.Delete(extracted, recursive: true);
            if (!await RunInDirAsync(libDir, appImage, "--appimage-extract")) return false;
            if (!Directory.Exists(extracted)) return false;

            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, recursive: true);
            Directory.Move(extracted, targetDir);

            var apphost = Directory
                .EnumerateFiles(targetDir, "AlbionPacketExplorer", SearchOption.AllDirectories)
                .FirstOrDefault(p => Path.GetFileName(p) == "AlbionPacketExplorer");
            if (apphost == null) return false;

            if (!await RunWaitAsync("pkexec", "setcap", "cap_net_raw,cap_net_admin+eip", apphost))
                return false;

            WriteLinuxDesktopEntry(home, targetDir, apphost);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteLinuxDesktopEntry(string home, string targetDir, string apphost)
    {
        try
        {
            var appsDir = Path.Combine(home, ".local", "share", "applications");
            Directory.CreateDirectory(appsDir);

            var icon = Directory
                .EnumerateFiles(targetDir, "*.png", SearchOption.AllDirectories)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p)
                    .Contains("icon", StringComparison.OrdinalIgnoreCase));

            var entry =
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Albion Packet Explorer\n" +
                $"Exec=\"{apphost}\"\n" +
                (icon != null ? $"Icon={icon}\n" : "") +
                "Categories=Network;Utility;\n" +
                "Terminal=false\n";

            var desktopPath = Path.Combine(appsDir, "albionpacketexplorer.desktop");
            File.WriteAllText(desktopPath, entry);
            _ = RunWaitAsync("update-desktop-database", appsDir);   // best effort; ignore result
        }
        catch
        {
            // A missing menu entry is not fatal; the setcap binary still runs.
        }
    }

    // True when running as an AppImage: setcap cannot be applied to the FUSE-mounted binary, so the
    // dialog must steer the user to extract-and-setcap instead of offering one-click grant.
    public static bool IsLinuxAppImage() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));

    private static string? ResolveLinuxBinary()
    {
        if (IsLinuxAppImage()) return null;
        var path = Environment.ProcessPath;
        return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : path;
    }

    private static async Task<bool> RunAndCheckAsync(ProcessStartInfo psi)
    {
        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Task<bool> RunWaitAsync(string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return RunAndCheckAsync(psi);
    }

    private static Task<bool> RunInDirAsync(string workingDir, string file, params string[] args)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return RunAndCheckAsync(psi);
    }
}
