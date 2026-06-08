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
    string? SkippedUpdateVersion = null)
{
    public static readonly AppSettings Default = new();
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
