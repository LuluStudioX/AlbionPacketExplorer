using System.Text.RegularExpressions;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Finds the installed Albion Online client and its IL2CPP <c>global-metadata.dat</c>. Auto-detects
/// the common install locations per OS and, when several installs exist (e.g. a stale standalone
/// plus a current Steam copy), prefers the newest version. A user-supplied path always wins and may
/// point at the game folder, the install root, or the metadata file itself.
/// </summary>
public static partial class AlbionClientLocator
{
    public sealed record AlbionClient(string MetadataPath, string GameDir, string? Version);

    private static readonly string MetaRelative =
        Path.Combine("Albion-Online_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

    private const string MetaFileName = "global-metadata.dat";

    /// <summary>
    /// Resolves the client to scan. If <paramref name="overridePath"/> is set and valid it is used;
    /// otherwise the newest auto-detected install is returned. Null if nothing usable is found.
    /// </summary>
    public static AlbionClient? Locate(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            var meta = ResolveMetadata(overridePath.Trim());
            return meta != null ? Build(meta) : null;
        }

        return CandidateGameDirs()
            .Select(dir => Path.Combine(dir, MetaRelative))
            .Where(File.Exists)
            .Select(Build)
            .OrderByDescending(c => ParseVersion(c.Version))
            .FirstOrDefault();
    }

    /// <summary>Reads the four-part client version from a game folder's <c>version.txt</c>, if present.</summary>
    public static string? ReadVersion(string gameDir)
    {
        try
        {
            var file = Path.Combine(gameDir, "version.txt");
            if (!File.Exists(file)) return null;
            var m = VersionPattern().Match(File.ReadAllText(file));
            return m.Success ? m.Value : null;
        }
        catch { return null; }
    }

    private static AlbionClient Build(string metadataPath)
    {
        var gameDir = GameDirOf(metadataPath);
        return new AlbionClient(metadataPath, gameDir, ReadVersion(gameDir));
    }

    // Accepts: the metadata file, a "game" folder, an install root, or the Metadata folder.
    private static string? ResolveMetadata(string path)
    {
        try
        {
            if (File.Exists(path) &&
                string.Equals(Path.GetFileName(path), MetaFileName, StringComparison.OrdinalIgnoreCase))
                return path;

            if (Directory.Exists(path))
            {
                var viaGame = Path.Combine(path, MetaRelative);
                if (File.Exists(viaGame)) return viaGame;

                var direct = Path.Combine(path, MetaFileName);
                if (File.Exists(direct)) return direct;
            }
        }
        catch { }
        return null;
    }

    // metadata sits at <game>/Albion-Online_Data/il2cpp_data/Metadata/global-metadata.dat
    private static string GameDirOf(string metadataPath)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(metadataPath)!);
        for (int i = 0; i < 3 && dir.Parent != null; i++) dir = dir.Parent;
        return dir.FullName;
    }

    private static IEnumerable<string> CandidateGameDirs()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in RawCandidates())
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            if (seen.Add(dir) && Directory.Exists(dir))
                yield return dir;
        }
    }

    private static IEnumerable<string> RawCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            // Standalone installer drops "AlbionOnline\game" under Program Files on any fixed drive.
            foreach (var drive in FixedDriveRoots())
            {
                yield return Path.Combine(drive, "Program Files (x86)", "AlbionOnline", "game");
                yield return Path.Combine(drive, "Program Files", "AlbionOnline", "game");
            }
            foreach (var lib in SteamLibraries())
                yield return Path.Combine(lib, "steamapps", "common", "Albion Online", "game");
        }
        else if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine("/Applications", "Albion Online.app", "Contents", "Resources", "Data", "game");
            yield return Path.Combine(home, "Applications", "Albion Online.app", "Contents", "Resources", "Data", "game");
        }
        else // Linux (Steam Proton)
        {
            foreach (var lib in SteamLibraries())
                yield return Path.Combine(lib, "steamapps", "common", "Albion Online", "game");
        }
    }

    private static IEnumerable<string> FixedDriveRoots()
    {
        IEnumerable<DriveInfo> drives;
        try { drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady); }
        catch { yield break; }
        foreach (var d in drives) yield return d.RootDirectory.FullName;
    }

    // Steam can place libraries on any drive; libraryfolders.vdf lists them all.
    private static IEnumerable<string> SteamLibraries()
    {
        foreach (var steam in SteamRoots())
        {
            yield return steam;
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            string text;
            try { text = File.ReadAllText(vdf); } catch { continue; }
            foreach (Match m in LibraryPathPattern().Matches(text))
                yield return m.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in FixedDriveRoots())
            {
                yield return Path.Combine(drive, "Program Files (x86)", "Steam");
                yield return Path.Combine(drive, "Program Files", "Steam");
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Path.Combine(home, ".steam", "steam");
            yield return Path.Combine(home, ".local", "share", "Steam");
            yield return Path.Combine(home, "Library", "Application Support", "Steam");
        }
    }

    private static Version ParseVersion(string? v) =>
        Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);

    [GeneratedRegex(@"\d+\.\d+\.\d+\.\d+")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"")]
    private static partial Regex LibraryPathPattern();
}
