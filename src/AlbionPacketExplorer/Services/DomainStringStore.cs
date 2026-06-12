using System.Reflection;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Resolves STRING-typed packet params whose values are game-domain identifiers (not numbers, not
/// <c>@LOC</c> keys) to readable English, from the embedded <c>Assets/domain-strings.json</c>
/// (sourced from ao-bin-dumps). E.g. an access-status param value <c>owner</c> -> "Owner",
/// <c>noaccess</c> -> "No access". A param is tagged <c>resolveAs: "str:&lt;set&gt;"</c> (e.g.
/// <c>str:accessrights</c>). Sibling of <see cref="ResolveEnumStore"/> (int enums) and
/// <see cref="LocStringStore"/> (@LOC keys).
/// </summary>
public sealed class DomainStringStore
{
    public const string Prefix = "str:";

    // setName -> (rawValue -> English meaning)
    private Dictionary<string, Dictionary<string, string>> _sets = new();

    /// <summary>Names of every value-set, sorted; empty until <see cref="Load"/>.</summary>
    public IReadOnlyList<string> SetNames { get; private set; } = [];

    public void Load()
    {
        _sets = LoadEmbedded();
        SetNames = _sets.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>True if <paramref name="resolveAs"/> targets a known string value-set.</summary>
    public bool IsStringResolve(string resolveAs)
        => resolveAs.StartsWith(Prefix, StringComparison.Ordinal)
           && _sets.ContainsKey(resolveAs[Prefix.Length..]);

    /// <summary>Resolves a raw string value for a <c>str:&lt;set&gt;</c> resolveAs to its meaning.</summary>
    public bool TryResolve(string resolveAs, string value, out string meaning)
    {
        meaning = string.Empty;
        if (string.IsNullOrEmpty(value)) return false;
        if (!resolveAs.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var name = resolveAs[Prefix.Length..];
        return _sets.TryGetValue(name, out var values) && values.TryGetValue(value, out meaning!);
    }

    /// <summary>Editor option strings: <c>str:&lt;set&gt;</c> per value-set.</summary>
    public IEnumerable<string> ResolveAsOptions() => SetNames.Select(n => Prefix + n);

    private static Dictionary<string, Dictionary<string, string>> LoadEmbedded()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("domain-strings.json", StringComparison.Ordinal));
            if (resName == null) return new();

            using var stream = asm.GetManifestResourceStream(resName)!;
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
                   ?? new();
        }
        catch { return new(); }
    }
}
