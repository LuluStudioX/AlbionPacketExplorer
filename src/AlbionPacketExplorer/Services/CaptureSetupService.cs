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
        psi.ArgumentList.Add("do shell script \"chmod o+r /dev/bpf*\" with administrator privileges");
        return await RunAndCheckAsync(psi);
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
}
