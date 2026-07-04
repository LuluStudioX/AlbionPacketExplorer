using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Central source of truth for every on-disk location the app uses. All data lives under a single
/// base directory that the user can relocate. The override pointer itself is stored at the fixed
/// default location (a tiny <c>location.json</c>) so it survives even after the base moves.
/// </summary>
public static class AppPaths
{
    // Velopack installs the app into %LocalAppData%\<PackId> (PackId == "AlbionPacketExplorer").
    // App DATA must live in a SEPARATE folder: when it shared that directory, our logs/icons
    // blocked Velopack's install/repair/uninstall ("failed to remove existing application
    // directory"). Data now lives in a sibling folder and is migrated out of the legacy location.
    private const string AppFolderName = "AlbionPacketExplorerData";
    private const string LegacyAppFolderName = "AlbionPacketExplorer"; // == Velopack install dir

    /// <summary>File names that belong to the app and are moved when the base is relocated.</summary>
    private static readonly string[] DataFileNames =
    [
        "settings.json", "layout.json", "filter-presets.json", "filter-last.json",
        "row-hidden.json", "row-hide-presets.json", "items.json", "packet-schema.user.json",
        "enum-labels.json", "code-notes.json", "protocol-scan.json", "protocol-overrides.json",
        "protocol-active.json"
    ];

    private static readonly string[] DataSubDirs = ["icons", "logs", "lang", "protocol-snapshots"];

    /// <summary>The fixed default base, used when no override is set. Always under LocalAppData.</summary>
    public static string DefaultBaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>The pre-relocation data folder, which is also Velopack's install directory.</summary>
    private static string LegacyBaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        LegacyAppFolderName);

    // The pointer to a relocated base lives at the fixed default, never inside the (movable) base.
    private static string LocationFile => Path.Combine(DefaultBaseDir, "location.json");

    // One-time move of our data out of the Velopack-managed legacy folder. Runs before the override
    // is loaded so a relocated location.json is read from the new base.
    private static readonly bool _migrated = MigrateLegacyBaseIfNeeded();

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
    public static string UserSchema       => Path.Combine(BaseDir, "packet-schema.user.json");
    public static string EnumLabels       => Path.Combine(BaseDir, "enum-labels.json");
    public static string CodeNotes        => Path.Combine(BaseDir, "code-notes.json");
    public static string ProtocolState    => Path.Combine(BaseDir, "protocol-scan.json");
    public static string ProtocolOverrides => Path.Combine(BaseDir, "protocol-overrides.json");
    /// <summary>Pointer to the protocol era fresh captures are stamped with (last scanned client).</summary>
    public static string ProtocolActive   => Path.Combine(BaseDir, "protocol-active.json");
    /// <summary>One JSON per client protocol revision (enum ordinals), keyed by fingerprint, so an old
    /// capture stays readable after a patch renumbers codes.</summary>
    public static string ProtocolSnapshotsDir => Path.Combine(BaseDir, "protocol-snapshots");
    public static string LangDir          => Path.Combine(BaseDir, "lang");
    public static string LogsDir          => Path.Combine(BaseDir, "logs");

    /// <summary>The item-name cache. Defaults to the base folder; can be overridden separately.</summary>
    public static string ItemCache =>
        string.IsNullOrWhiteSpace(_override.ItemCacheDir)
            ? Path.Combine(BaseDir, "items.json")
            : Path.Combine(_override.ItemCacheDir, "items.json");

    /// <summary>The item-icon cache folder. Defaults to base/icons; can be overridden (e.g. to
    /// reuse another app's already-downloaded Albion icons).</summary>
    public static string IconCacheDir =>
        string.IsNullOrWhiteSpace(_override.IconCacheDir)
            ? Path.Combine(BaseDir, "icons")
            : _override.IconCacheDir;

    public static bool IconCacheIsCustom => !string.IsNullOrWhiteSpace(_override.IconCacheDir);

    /// <summary>Default folder the "open capture" picker starts in (the app's own saved captures).</summary>
    public static string DefaultLogFolder => LogsDir;

    /// <summary>Folder the app points users to when opening a saved capture (override-able).</summary>
    public static string AlbionLogFolder =>
        string.IsNullOrWhiteSpace(_override.LogFolder) ? DefaultLogFolder : _override.LogFolder;

    public static bool ItemCacheIsCustom => !string.IsNullOrWhiteSpace(_override.ItemCacheDir);

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

    /// <summary>
    /// Overrides the icon cache folder. When <paramref name="migrate"/> is set the existing icon
    /// PNGs are copied to the new folder. Empty/default clears the override. Pointing at a folder
    /// that already holds icons (e.g. another app's cache) lets them be reused as-is.
    /// </summary>
    public static bool SetIconCacheDir(string? folder, bool migrate, out string? error)
    {
        error = null;
        try
        {
            var oldDir = IconCacheDir;
            var dir = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder.Trim());
            if (string.Equals(dir, Path.Combine(BaseDir, "icons"), StringComparison.OrdinalIgnoreCase)) dir = null;

            if (dir != null) Directory.CreateDirectory(dir);

            var migrateFrom = oldDir;
            _override = _override with { IconCacheDir = dir };
            SaveOverride();

            if (migrate && Directory.Exists(migrateFrom) &&
                !string.Equals(migrateFrom, IconCacheDir, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(IconCacheDir);
                foreach (var file in Directory.EnumerateFiles(migrateFrom, "*.png"))
                {
                    var dst = Path.Combine(IconCacheDir, Path.GetFileName(file));
                    File.Copy(file, dst, overwrite: true);
                    // Verify the copy before removing the original, then move (not duplicate).
                    if (File.Exists(dst)) TryDelete(file);
                }
                // Remove the now-empty source folder so nothing is left behind.
                TryDeleteDir(migrateFrom);
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

    // One-time relocation of app data out of the Velopack-managed install folder (see AppFolderName
    // remarks). Best-effort: any failure leaves data where it is and the app still runs.
    private static bool MigrateLegacyBaseIfNeeded()
    {
        try
        {
            if (string.Equals(DefaultBaseDir, LegacyBaseDir, StringComparison.OrdinalIgnoreCase))
                return false;
            // New base already initialised (migrated earlier, or fresh install): nothing to do.
            if (File.Exists(Path.Combine(DefaultBaseDir, "settings.json"))) return false;

            var legacyHasData =
                File.Exists(Path.Combine(LegacyBaseDir, "location.json")) ||
                DataFileNames.Any(n => File.Exists(Path.Combine(LegacyBaseDir, n))) ||
                DataSubDirs.Any(d => Directory.Exists(Path.Combine(LegacyBaseDir, d)));
            if (!legacyHasData) return false;

            Directory.CreateDirectory(DefaultBaseDir);
            foreach (var name in DataFileNames)
                TryMoveFile(Path.Combine(LegacyBaseDir, name), Path.Combine(DefaultBaseDir, name));
            TryMoveFile(Path.Combine(LegacyBaseDir, "location.json"),
                        Path.Combine(DefaultBaseDir, "location.json"));
            foreach (var sub in DataSubDirs)
                TryMoveDir(Path.Combine(LegacyBaseDir, sub), Path.Combine(DefaultBaseDir, sub));
            return true;
        }
        catch { return false; }
    }

    private static void TryMoveFile(string src, string dst)
    {
        try
        {
            if (!File.Exists(src)) return;
            File.Copy(src, dst, overwrite: true);
            if (File.Exists(dst)) TryDelete(src);
        }
        catch { }
    }

    // Prefer an atomic rename (instant, same volume); fall back to per-file copy if the destination
    // already exists.
    private static void TryMoveDir(string srcDir, string dstDir)
    {
        try
        {
            if (!Directory.Exists(srcDir)) return;
            if (!Directory.Exists(dstDir))
            {
                Directory.Move(srcDir, dstDir);
                return;
            }
            foreach (var file in Directory.EnumerateFiles(srcDir))
            {
                var dst = Path.Combine(dstDir, Path.GetFileName(file));
                try { File.Copy(file, dst, overwrite: true); if (File.Exists(dst)) TryDelete(file); }
                catch { }
            }
            TryDeleteDir(srcDir);
        }
        catch { }
    }

    private static void SaveOverride()
    {
        // The pointer always lives at the fixed default so it is found regardless of where the
        // base moved to. If nothing is overridden, the file is removed entirely.
        Directory.CreateDirectory(DefaultBaseDir);
        if (string.IsNullOrWhiteSpace(_override.BaseDir) &&
            string.IsNullOrWhiteSpace(_override.LogFolder) &&
            string.IsNullOrWhiteSpace(_override.ItemCacheDir) &&
            string.IsNullOrWhiteSpace(_override.IconCacheDir))
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
        string? ItemCacheDir = null,
        string? IconCacheDir = null);
}
