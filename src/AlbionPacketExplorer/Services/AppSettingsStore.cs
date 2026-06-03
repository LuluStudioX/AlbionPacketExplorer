using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public enum DetailDensity
{
    Compact,
    Normal,
    Comfortable
}

public record AppSettings(
    bool ResolveItemNames = false,
    bool ResolveIcons = false,
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
    string ToggleRowExpandGesture = "Space")
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
