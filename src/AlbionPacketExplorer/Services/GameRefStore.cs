using System.Reflection;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Read-only reference-data resolver: turns a raw wire code into a game name across several domains,
/// each a compact embedded map (code -> name) distilled from ao-bin-dumps:
/// <list type="bullet">
/// <item><c>location</c> - world cluster @Index -> cluster name (e.g. "1359" -> "Rivercopse Fount").</item>
/// <item><c>mob</c> / <c>spell</c> / <c>building</c> - entity @uniquename -> readable name.</item>
/// </list>
/// A param tagged <c>resolveAs: "location"|"mob"|"spell"|"building"</c> uses this. Same shape as
/// <see cref="ResolveEnumStore"/> but keyed by string codes (a location index or a uniquename), and
/// static, so it runs without game data and is not gated by the item-name toggle.
/// </summary>
public sealed class GameRefStore
{
    // resolveAs tag -> embedded asset (matched by file-name suffix). Location is keyed by the numeric
    // cluster index; the rest by @uniquename. Adding a domain is one line + one asset.
    private static readonly (string Domain, string Asset)[] Sources =
    [
        ("location", "clusters.json"),
        ("mob", "mob-names.json"),
        ("spell", "spell-names.json"),
        ("building", "building-names.json"),
    ];

    private Dictionary<string, Dictionary<string, string>> _domains = new();

    public void Load()
    {
        var loaded = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (domain, asset) in Sources)
            loaded[domain] = LoadEmbedded(asset);
        _domains = loaded;
    }

    /// <summary>True when <paramref name="resolveAs"/> names one of the reference domains.</summary>
    public bool IsRefResolve(string resolveAs) => _domains.ContainsKey(resolveAs);

    /// <summary>Resolves a wire code (as string) for a domain to its name; false when unknown.</summary>
    public bool TryResolve(string resolveAs, string code, out string name)
    {
        name = string.Empty;
        return _domains.TryGetValue(resolveAs, out var map) && map.TryGetValue(code, out name!);
    }

    /// <summary>The resolveAs option strings for the param editor (one per domain).</summary>
    public IEnumerable<string> ResolveAsOptions()
        => _domains.Keys.OrderBy(k => k, StringComparer.Ordinal);

    private static Dictionary<string, string> LoadEmbedded(string assetSuffix)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(assetSuffix, StringComparison.Ordinal));
            if (resName == null) return new();
            using var stream = asm.GetManifestResourceStream(resName)!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        }
        catch { return new(); }
    }
}
