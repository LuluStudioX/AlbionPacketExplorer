using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack;
using Velopack.Sources;

namespace AlbionPacketExplorer.Services;

public sealed class UpdateService
{
    // The update feed is served from our own site (a static Velopack feed behind a Cloudflare
    // tunnel), so the GitHub source repo can stay private and no token ever ships in the client.
    // Override per machine with APX_UPDATE_URL; otherwise the baked-in default is used.
    private const string DefaultFeedUrl = "https://projects.lulustudio.dk/apx/feed";

    private static string FeedUrl =>
        Environment.GetEnvironmentVariable("APX_UPDATE_URL") is { Length: > 0 } u ? u : DefaultFeedUrl;

    private readonly UpdateManager _mgr;

    public UpdateService()
    {
        // Each platform/arch is packed under its own Velopack channel (win-x64, linux-x64,
        // osx-x64, osx-arm64), so the updater asks for the channel matching the running RID.
        _mgr = new UpdateManager(
            new SimpleWebSource(FeedUrl),
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

    // NewVersion set when an update is available; Error set when the check itself failed (feed
    // unreachable, bad URL, not installed via Velopack, ...). Both null = up to date.
    // Notes carries the target release's changelog (markdown) when the package was built with
    // release notes (vpk pack --releaseNotes); null/empty when none were attached.
    public sealed record UpdateCheckResult(string? NewVersion, string? Error, string? Notes = null);

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            var info = await _mgr.CheckForUpdatesAsync();
            if (info == null) return new UpdateCheckResult(null, null);

            // Prefer the full changelog across every skipped version; fall back to the target's own
            // notes when the feed lists a single release or can't be read.
            var notes = await BuildAggregatedNotesAsync(info)
                        ?? info.TargetFullRelease.NotesMarkdown;
            return new UpdateCheckResult(info.TargetFullRelease.Version.ToString(), null, notes);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(null, ex.Message);
        }
    }

    // Concatenate the release notes of every Full release newer than the installed version, up to and
    // including the target, newest first. A multi-version jump (e.g. v0.12.0 -> v0.12.3) then shows
    // every intermediate version's changelog instead of only the latest. Returns null (caller falls
    // back to the target notes) when the feed is unreachable, lists one release, or yields nothing.
    private async Task<string?> BuildAggregatedNotesAsync(UpdateInfo info)
    {
        try
        {
            var current = ParseVersion(_mgr.CurrentVersion?.ToString());
            var target = ParseVersion(info.TargetFullRelease.Version.ToString());
            if (target == null) return null;

            var url = $"{FeedUrl.TrimEnd('/')}/releases.{CurrentChannel()}.json";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            await using var stream = await http.GetStreamAsync(url);
            var feed = await JsonSerializer.DeserializeAsync<FeedDocument>(stream, FeedJson);
            if (feed?.Assets is not { Count: > 1 }) return null;

            var blocks = feed.Assets
                .Where(a => string.Equals(a.Type, "Full", StringComparison.OrdinalIgnoreCase))
                .Where(a => !string.IsNullOrWhiteSpace(a.NotesMarkdown))
                .Select(a => (Version: ParseVersion(a.Version), a.NotesMarkdown))
                .Where(a => a.Version != null
                            && a.Version <= target
                            && (current == null || a.Version > current))
                .OrderByDescending(a => a.Version)
                .Select(a => a.NotesMarkdown!.Trim())
                .ToList();

            var combined = string.Join("\n\n", blocks);
            return string.IsNullOrWhiteSpace(combined) ? null : combined;
        }
        catch
        {
            return null;
        }
    }

    // Compare on the numeric core only; released tags are clean X.Y.Z, and a stray pre-release/build
    // suffix should not break ordering.
    private static Version? ParseVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var core = raw.Split('-', '+')[0];
        return Version.TryParse(core, out var v) ? v : null;
    }

    private static readonly JsonSerializerOptions FeedJson = new() { PropertyNameCaseInsensitive = true };

    // Minimal shape of Velopack's releases.<channel>.json: only the fields needed to aggregate notes.
    private sealed record FeedDocument(
        [property: JsonPropertyName("Assets")] List<FeedAsset>? Assets);

    private sealed record FeedAsset(
        [property: JsonPropertyName("Version")] string? Version,
        [property: JsonPropertyName("Type")] string? Type,
        [property: JsonPropertyName("NotesMarkdown")] string? NotesMarkdown);

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
