using System.Text.RegularExpressions;

namespace AlbionPacketExplorer.Services;

/// <summary>A folder found to contain a usable Albion item-icon cache.</summary>
public sealed record IconCacheCandidate(string Path, int IconCount);

/// <summary>
/// Best-effort scanner that hunts the machine for an existing Albion item-icon cache (from this
/// app, SAT, ao-bin-dumps clones, other tools, etc.) so the user can reuse those PNGs instead of
/// re-downloading thousands of files. Identifies a cache by sampling PNG names against Albion's
/// item-name shape; cheap and bounded so it never walks the whole disk.
/// </summary>
public static class IconCacheLocator
{
    // Albion icon names carry distinctive markers: a tier prefix (T1_..T8_), or one of a set of
    // Albion-specific tokens (FARM, FURNITUREITEM, MOUNT, ARTEFACT, the city flag names, etc.).
    // Requiring one of these avoids matching generic snake_case PNGs from unrelated apps.
    private static readonly Regex ItemNamePattern = new(
        @"(^T\d_)" +
        @"|(^(UNIQUE|QUESTITEM|RANDOM|PLAYERISLAND|SKIN|MAIN|HEAD|ARMOR|SHOES|2H|1H|OFF)_)" +
        @"|(_(FARM|MOUNT|ARTEFACT|FURNITUREITEM|JOURNAL|TREASURE|TOOL|CAPE|BAG|POTION|MEAL|HORSE|ARMORED)_?)" +
        @"|(_(BRIDGEWATCH|CAERLEON|FORT_STERLING|LYMHURST|MARTLOCK|THETFORD))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Require a majority of sampled PNGs to look like Albion items so unrelated PNG dirs (Office
    // package resources, app skins, etc.) never qualify, while tolerating misc extras.
    private const int MinMatchRatioPercent = 55;
    private const int MinMatches = 15;
    private const int SampleSize = 400;
    private const int MaxDepth = 7;

    /// <summary>
    /// Scans a bounded set of likely roots and returns matching icon folders, best (most icons)
    /// first. The app's own current folder is excluded. Safe to call on a background thread.
    /// </summary>
    public static List<IconCacheCandidate> Find(CancellationToken token = default)
    {
        var results = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var current = NormalizeOrNull(AppPaths.IconCacheDir);

        foreach (var root in CandidateRoots())
        {
            if (token.IsCancellationRequested) break;
            if (!SafeDirExists(root)) continue;
            ScanRoot(root, results, current, token);
        }

        return results
            .Select(kv => new IconCacheCandidate(kv.Key, kv.Value))
            .OrderByDescending(c => c.IconCount)
            .ToList();
    }

    private static IEnumerable<string> CandidateRoots()
    {
        var local   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Known tool data dirs (shallow, high-signal).
        yield return Path.Combine(local, "StatisticsAnalysisTool");
        yield return Path.Combine(roaming, "StatisticsAnalysisTool");
        yield return local;
        yield return roaming;

        // Common dev-folder names where people clone repos that ship Albion icons.
        string[] devSubpaths =
        [
            Path.Combine("Documents", "github"),
            Path.Combine("Documents", "GitHub"),
            "github", "GitHub", "git", "repos",
            Path.Combine("source", "repos"),
        ];

        // The profile FOLDER name can differ from the account name (e.g. login "Lulu" but
        // C:\Users\bimas), so derive it from the real profile path rather than UserName.
        var profileParent = Path.GetDirectoryName(profile);            // e.g. C:\Users
        var profileLeaf   = Path.GetFileName(profile);                  // e.g. bimas
        var usersRel      = profileParent != null && profileLeaf.Length > 0
            ? Path.Combine(Path.GetFileName(profileParent), profileLeaf) // Users\bimas
            : null;

        // Probe the profile itself, plus the same Users\<leaf> on every fixed drive (repos often
        // live off C:, e.g. D:\Users\bimas\Documents\github), and drive-root dev folders.
        foreach (var sub in devSubpaths)
            yield return Path.Combine(profile, sub);

        foreach (var drive in FixedDriveRoots())
        {
            if (usersRel != null)
            {
                var userDir = Path.Combine(drive, usersRel);
                foreach (var sub in devSubpaths)
                    yield return Path.Combine(userDir, sub);
            }
            foreach (var sub in devSubpaths)
                yield return Path.Combine(drive, sub);
        }
    }

    private static IEnumerable<string> FixedDriveRoots()
    {
        string[] drives;
        try
        {
            drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToArray();
        }
        catch { drives = ["C:\\"]; }
        return drives;
    }

    // Walk a root breadth-limited, scoring any directory that holds Albion-looking PNGs. Skips
    // hugely-branching trees by capping depth.
    private static void ScanRoot(string root, Dictionary<string, int> results, string? exclude,
        CancellationToken token)
    {
        var queue = new Queue<(string dir, int depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            if (token.IsCancellationRequested) return;
            var (dir, depth) = queue.Dequeue();

            int count = CountAlbionIcons(dir);
            if (count > 0)
            {
                var norm = NormalizeOrNull(dir);
                if (norm != null && !string.Equals(norm, exclude, StringComparison.OrdinalIgnoreCase))
                    results[norm] = count;
            }

            if (depth >= MaxDepth) continue;
            foreach (var sub in SafeSubDirs(dir))
                queue.Enqueue((sub, depth + 1));
        }
    }

    // Sample up to N PNGs; if at least half look like Albion item names, treat the whole folder
    // as a cache and return its total PNG count (so the richest folder sorts first).
    private static int CountAlbionIcons(string dir)
    {
        try
        {
            int sampled = 0, matched = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
            {
                if (ItemNamePattern.IsMatch(Path.GetFileNameWithoutExtension(file))) matched++;
                if (++sampled >= SampleSize) break;
            }
            if (sampled == 0 || matched < MinMatches) return 0;
            if (matched * 100 / sampled < MinMatchRatioPercent) return 0;
            return SafeFileCount(dir);
        }
        catch { return 0; }
    }

    private static int SafeFileCount(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.png").Count(); }
        catch { return 0; }
    }

    private static IEnumerable<string> SafeSubDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return []; }
    }

    private static bool SafeDirExists(string dir)
    {
        try { return Directory.Exists(dir); }
        catch { return false; }
    }

    private static string? NormalizeOrNull(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
        catch { return null; }
    }
}
