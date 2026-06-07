using System.Text.RegularExpressions;

namespace AlbionPacketExplorer.Services;

/// <summary>Which byte-key indices a reference C# class for a code reads, and where.</summary>
public sealed record ReferenceDiffResult(string ClassName, string FilePath, SortedSet<int> SourceReads);

/// <summary>
/// Best-effort reader of an external C# project's existing event/operation constructors so we can
/// flag which observed fields that project does not yet parse. Point it at a local checkout via
/// <c>APX_REFERENCE_REPO</c> (or a sibling clone). It matches the common Photon access patterns
/// <c>parameters.TryGetValue(n, ...)</c> / <c>parameters[n]</c>.
/// </summary>
public static class ReferenceConstructorReader
{
    public static string? FindRepo()
    {
        var env = Environment.GetEnvironmentVariable("APX_REFERENCE_REPO");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        foreach (var cand in new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "reference-source"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", ".."),
        })
        {
            try { var full = Path.GetFullPath(cand); if (Directory.Exists(full)) return full; }
            catch { /* ignore */ }
        }
        return null;
    }

    public static ReferenceDiffResult? Read(string repo, string kind, string eventName)
    {
        if (!Directory.Exists(repo)) return null;

        var candidates = kind == "EVENT"
            ? new[] { eventName + "Event", eventName }
            : new[] { eventName + "Operation", eventName + "Response", eventName + "Request", eventName };

        foreach (var cn in candidates)
        {
            var file = FindClassFile(repo, cn);
            if (file == null) continue;

            var text = File.ReadAllText(file);
            var reads = new SortedSet<int>();
            foreach (Match m in Regex.Matches(text, @"TryGetValue\(\s*(\d+)"))
                if (int.TryParse(m.Groups[1].Value, out var n)) reads.Add(n);
            foreach (Match m in Regex.Matches(text, @"[Pp]arameters\s*\[\s*(\d+)\s*\]"))
                if (int.TryParse(m.Groups[1].Value, out var n)) reads.Add(n);

            return new ReferenceDiffResult(cn, file, reads);
        }
        return null;
    }

    private static string? FindClassFile(string root, string className)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                if (Path.GetFileNameWithoutExtension(f).Equals(className, StringComparison.OrdinalIgnoreCase))
                    return f;

            foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                if (Regex.IsMatch(File.ReadAllText(f), $@"class\s+{Regex.Escape(className)}\b"))
                    return f;
        }
        catch { /* ignore */ }
        return null;
    }
}
