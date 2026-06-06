using System.Runtime.InteropServices;
using Velopack;
using Velopack.Sources;

namespace AlbionPacketExplorer.Services;

public sealed class UpdateService
{
    private const string GithubRepo = "https://github.com/LuluStudioX/AlbionPacketExplorer";

    private readonly UpdateManager _mgr;

    public UpdateService()
    {
        // Each platform/arch is packed under its own Velopack channel (win-x64, linux-x64,
        // osx-x64, osx-arm64) so the four release jobs no longer overwrite each other's
        // identically-named assets. The updater must therefore ask for the channel that matches
        // the running RID rather than Velopack's OS-only default.
        _mgr = new UpdateManager(
            new GithubSource(GithubRepo, null, false),
            new UpdateOptions { ExplicitChannel = CurrentChannel() });
    }

    // os-arch channel id, matching the --channel passed to `vpk pack` in the release workflow.
    private static string CurrentChannel()
    {
        var os = OperatingSystem.IsWindows() ? "win"
               : OperatingSystem.IsMacOS()   ? "osx"
               :                               "linux";
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";
        return $"{os}-{arch}";
    }

    // Returns the new version string if an update is available, null otherwise.
    public async Task<string?> CheckForUpdateAsync()
    {
        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            return info?.TargetFullRelease.Version.ToString();
        }
        catch { return null; }
    }

    // Downloads and applies update, then restarts the app.
    public async Task ApplyUpdateAsync(IProgress<int>? progress = null)
    {
        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info == null) return;
            await _mgr.DownloadUpdatesAsync(info, progress == null ? null : p => progress.Report(p));
            _mgr.ApplyUpdatesAndRestart(info);
        }
        catch { }
    }
}
