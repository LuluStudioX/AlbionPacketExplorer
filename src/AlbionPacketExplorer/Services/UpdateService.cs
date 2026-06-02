using Velopack;
using Velopack.Sources;

namespace AlbionPacketExplorer.Services;

public sealed class UpdateService
{
    private const string GithubRepo = "https://github.com/LuluStudioX/AlbionPacketExplorer";

    private readonly UpdateManager _mgr;

    public UpdateService()
    {
        _mgr = new UpdateManager(new GithubSource(GithubRepo, null, false));
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
