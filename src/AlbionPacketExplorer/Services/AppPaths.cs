using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Central source of truth for every on-disk location the app uses. All data lives under a single
/// base directory that the user can relocate. The override pointer itself is stored at the fixed
/// default location (a tiny <c>location.json</c>) so it survives even after the base moves.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "AlbionPacketExplorer";

    /// <summary>The fixed default base, used when no override is set. Always under LocalAppData.</summary>
    public static string DefaultBaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    // The pointer to a relocated base lives at the fixed default, never inside the (movable) base.
    private static string LocationFile => Path.Combine(DefaultBaseDir, "location.json");

    private static LocationOverride _override = LoadOverride();

    /// <summary>The active base directory all data paths derive from.</summary>
    public static string BaseDir =>
        string.IsNullOrWhiteSpace(_override.BaseDir) ? DefaultBaseDir : _override.BaseDir;

    public static string SettingsFile     => Path.Combine(BaseDir, "settings.json");
    public static string LayoutFile       => Path.Combine(BaseDir, "layout.json");
    public static string FilterPresets    => Path.Combine(BaseDir, "filter-presets.json");
    public static string LastFilter       => Path.Combine(BaseDir, "filter-last.json");
    public static string RowHidden        => Path.Combine(BaseDir, "row-hidden.json");
    public static string RowHidePresets   => Path.Combine(BaseDir, "row-hide-presets.json");
    public static string IconCacheDir     => Path.Combine(BaseDir, "icons");
    public static string LogsDir          => Path.Combine(BaseDir, "logs");

    /// <summary>The item-name cache. Defaults to the base folder; can be overridden separately.</summary>
    public static string ItemCache =>
        string.IsNullOrWhiteSpace(_override.ItemCacheDir)
            ? Path.Combine(BaseDir, "items.json")
            : Path.Combine(_override.ItemCacheDir, "items.json");

    /// <summary>Default folder where external Albion-traffic tools write their packet log.</summary>
    public static string DefaultLogFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "StatisticsAnalysisTool", "Instances");

    /// <summary>Folder the app points users to for opening external packet logs.</summary>
    public static string AlbionLogFolder =>
        string.IsNullOrWhiteSpace(_override.LogFolder) ? DefaultLogFolder : _override.LogFolder;

    public static bool ItemCacheIsCustom => !string.IsNullOrWhiteSpace(_override.ItemCacheDir);

    /// <summary>File names that belong to the app and are moved when the base is relocated.</summary>
    private static readonly string[] DataFileNames =
    [
        "settings.json", "layout.json", "filter-presets.json", "filter-last.json",
        "row-hidden.json", "row-hide-presets.json", "items.json"
    ];

    private static readonly string[] DataSubDirs = ["icons", "logs"];

    private static LocationOverride LoadOverride()
    {
        try
        {
            if (File.Exists(LocationFile))
            {
                var json = File.ReadAllText(LocationFile);
                return JsonSerializer.Deserialize<LocationOverride>(json) ?? new LocationOverride();
            }
        }
        catch { /* fall back to defaults */ }
        return new LocationOverride();
    }

    /// <summary>
    /// Relocates the data base to <paramref name="newBaseDir"/>. Optionally moves existing data
    /// files into the new location so the old folder is not left bloated. Persists the override.
    /// </summary>
    public static bool Relocate(string newBaseDir, bool migrate, out string? error)
    {
        error = null;
        try
        {
            newBaseDir = Path.GetFullPath(newBaseDir.Trim());
            if (string.Equals(newBaseDir, BaseDir, StringComparison.OrdinalIgnoreCase))
                return true;

            Directory.CreateDirectory(newBaseDir);
            if (migrate) MoveData(BaseDir, newBaseDir);

            _override = _override with
            {
                BaseDir = string.Equals(newBaseDir, DefaultBaseDir, StringComparison.OrdinalIgnoreCase)
                    ? null : newBaseDir
            };
            SaveOverride();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Overrides the external Albion log folder the app points users to. Empty = default.</summary>
    public static bool SetLogFolder(string? folder, out string? error)
    {
        error = null;
        try
        {
            var dir = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());
            _override = _override with
            {
                LogFolder = string.Equals(dir, DefaultLogFolder, StringComparison.OrdinalIgnoreCase) ? null : dir
            };
            SaveOverride();
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>
    /// Overrides the folder holding the item-name cache (items.json). Optionally moves the existing
    /// cache file. Empty/default folder clears the override (cache lives under the base again).
    /// </summary>
    public static bool SetItemCacheDir(string? folder, bool migrate, out string? error)
    {
        error = null;
        try
        {
            var oldFile = ItemCache;
            var dir = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());
            if (string.Equals(dir, BaseDir, StringComparison.OrdinalIgnoreCase)) dir = null;

            if (dir != null) Directory.CreateDirectory(dir);

            _override = _override with { ItemCacheDir = dir };
            SaveOverride();

            if (migrate && File.Exists(oldFile) && !string.Equals(oldFile, ItemCache, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(oldFile, ItemCache, overwrite: true);
                TryDelete(oldFile);
            }
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // Copy everything to the new location and verify each file landed before deleting any
    // original, so a failure mid-move never destroys data. Throws if any copy fails or a
    // destination cannot be verified; the caller reports it and leaves the override unchanged.
    private static void MoveData(string from, string to)
    {
        var copied = new List<(string src, string dst)>();

        foreach (var name in DataFileNames)
        {
            var src = Path.Combine(from, name);
            if (!File.Exists(src)) continue;
            var dst = Path.Combine(to, name);
            File.Copy(src, dst, overwrite: true);
            if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length)
                throw new IOException($"Failed to copy {name} to the new location.");
            copied.Add((src, dst));
        }

        foreach (var sub in DataSubDirs)
        {
            var srcDir = Path.Combine(from, sub);
            if (!Directory.Exists(srcDir)) continue;
            var dstDir = Path.Combine(to, sub);
            Directory.CreateDirectory(dstDir);
            foreach (var file in Directory.EnumerateFiles(srcDir))
            {
                var dst = Path.Combine(dstDir, Path.GetFileName(file));
                File.Copy(file, dst, overwrite: true);
                if (!File.Exists(dst))
                    throw new IOException($"Failed to copy {Path.GetFileName(file)} to the new location.");
                copied.Add((file, dst));
            }
        }

        // All copies verified: now remove the originals.
        foreach (var (src, _) in copied) TryDelete(src);
        foreach (var sub in DataSubDirs) TryDeleteDir(Path.Combine(from, sub));
    }

    private static void SaveOverride()
    {
        // The pointer always lives at the fixed default so it is found regardless of where the
        // base moved to. If nothing is overridden, the file is removed entirely.
        Directory.CreateDirectory(DefaultBaseDir);
        if (string.IsNullOrWhiteSpace(_override.BaseDir) &&
            string.IsNullOrWhiteSpace(_override.LogFolder) &&
            string.IsNullOrWhiteSpace(_override.ItemCacheDir))
        {
            TryDelete(LocationFile);
            return;
        }
        var json = JsonSerializer.Serialize(_override,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(LocationFile, json);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
        catch { }
    }

    private sealed record LocationOverride(
        string? BaseDir = null,
        string? LogFolder = null,
        string? ItemCacheDir = null);
}
