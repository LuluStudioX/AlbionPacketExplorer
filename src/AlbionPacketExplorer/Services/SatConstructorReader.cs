using System.Text.RegularExpressions;

namespace AlbionPacketExplorer.Services;

/// <summary>Which byte-key indices SAT's class for a code actually reads, and where.</summary>
public sealed record SatDiffResult(string ClassName, string FilePath, SortedSet<int> SatReads);

/// <summary>
/// Best-effort reader of SAT's existing event/operation constructors so we can flag which observed
/// fields SAT does not yet parse. Dev-oriented: it needs a local SAT checkout (env APX_SAT_REPO or
/// a sibling clone). SAT reads params as <c>parameters.TryGetValue(n, ...)</c> / <c>parameters[n]</c>.
/// </summary>
public static class SatConstructorReader
{
    public static string? FindRepo()
    {
        var env = Environment.GetEnvironmentVariable("APX_SAT_REPO");
        if (!string.IsNullOrWhiteSpace(env) && IsRepo(env)) return env;

        foreach (var cand in new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "AlbionOnline-StatisticsAnalysis"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "AlbionOnline-StatisticsAnalysis"),
        })
        {
            try { var full = Path.GetFullPath(cand); if (IsRepo(full)) return full; }
            catch { /* ignore */ }
        }
        return null;
    }

    private static bool IsRepo(string dir) =>
        Directory.Exists(Path.Combine(dir, "src", "StatisticsAnalysisTool", "Network"));

    public static SatDiffResult? Read(string repo, string kind, string eventName)
    {
        var networkDir = Path.Combine(repo, "src", "StatisticsAnalysisTool", "Network");
        if (!Directory.Exists(networkDir)) return null;

        var candidates = kind == "EVENT"
            ? new[] { eventName + "Event", eventName }
            : new[] { eventName + "Operation", eventName + "Response", eventName + "Request", eventName };

        foreach (var cn in candidates)
        {
            var file = FindClassFile(networkDir, cn);
            if (file == null) continue;

            var text = File.ReadAllText(file);
            var reads = new SortedSet<int>();
            foreach (Match m in Regex.Matches(text, @"TryGetValue\(\s*(\d+)"))
                if (int.TryParse(m.Groups[1].Value, out var n)) reads.Add(n);
            foreach (Match m in Regex.Matches(text, @"[Pp]arameters\s*\[\s*(\d+)\s*\]"))
                if (int.TryParse(m.Groups[1].Value, out var n)) reads.Add(n);

            return new SatDiffResult(cn, file, reads);
        }
        return null;
    }

    private static string? FindClassFile(string dir, string className)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                if (Path.GetFileNameWithoutExtension(f).Equals(className, StringComparison.OrdinalIgnoreCase))
                    return f;

            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                if (Regex.IsMatch(File.ReadAllText(f), $@"class\s+{Regex.Escape(className)}\b"))
                    return f;
        }
        catch { /* ignore */ }
        return null;
    }
}
