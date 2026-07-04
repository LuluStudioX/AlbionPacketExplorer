using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public enum DetailDensity
{
    Compact,
    Normal,
    Comfortable
}

/// <summary>How resolved item icons are handled.</summary>
public enum IconCacheMode
{
    /// <summary>Don't show icons at all.</summary>
    Off,
    /// <summary>Fetch icons but keep them in memory only for the session (no disk writes).</summary>
    Memory,
    /// <summary>Fetch icons and cache them to disk for reuse across sessions.</summary>
    Disk
}

public record AppSettings(
    bool ResolveItemNames = false,
    IconCacheMode IconMode = IconCacheMode.Disk,
    bool SidebarVisible = true,
    bool MinimizeToTray = false,
    bool IsDarkMode = true,
    bool ForceExpandRows = false,
    bool AutoStartCapture = false,
    bool AutoSaveLogs = false,
    DetailDensity Density = DetailDensity.Normal,
    string Culture = "en",
    string SidebarToggleGesture = "F5",
    string AutoSelectNewestGesture = "Ctrl+L",
    string ToggleRowExpandGesture = "Space",
    bool HasSeenWelcome = false,
    string AccentTheme = "Indigo",
    string? SkippedUpdateVersion = null,
    bool ProtocolScanEnabled = false,
    bool ProtocolScanOnStartup = false,
    string? ProtocolWebhookUrl = null, // deprecated: migrated into ProtocolWebhooks on load
    string? AlbionClientPath = null,
    string[]? ProtocolWebhooks = null,
    // Appended (not inserted) so the positional call site in MainViewModel.SaveSettings stays valid;
    // JSON round-trips by property name, so old settings files without this key default to false.
    bool ResolveEntityNames = false)
{
    public static readonly AppSettings Default = new();

    /// <summary>The configured webhooks, folding the legacy single-URL field in for back-compat.</summary>
    public IReadOnlyList<string> EffectiveWebhooks =>
        (ProtocolWebhooks ?? (string.IsNullOrWhiteSpace(ProtocolWebhookUrl) ? [] : [ProtocolWebhookUrl]))
        .Where(u => !string.IsNullOrWhiteSpace(u)).ToArray();
}

public static class AppSettingsStore
{
    private static string FilePath => AppPaths.SettingsFile;

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return AppSettings.Default;
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? AppSettings.Default;
        }
        catch
        {
            return AppSettings.Default;
        }
    }

    public static void Save(AppSettings s)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s));
        }
        catch { }
    }
}
