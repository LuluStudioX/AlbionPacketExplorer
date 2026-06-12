using System.Reflection;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Read-only catalogue of small, packet-relevant client enums (ActionComponentType, EquipmentSlot,
/// ...) harvested from the static client decode and embedded as <c>Assets/resolve-enums.json</c>.
/// A param tagged <c>resolveAs: "enum:&lt;Name&gt;"</c> uses this to turn a raw int into its member
/// name (e.g. EVENT 55 key 2 value 1 -> "CraftItem"). Distinct from <see cref="EnumLabelStore"/>,
/// which holds one-off user labels per (kind, code, key, value).
/// </summary>
public sealed class ResolveEnumStore
{
    public const string Prefix = "enum:";

    // enumName -> (value -> memberName). Values are kept as strings to match the wire int verbatim
    // (covers negative response codes like RcGeneric.InternalServerError = -500).
    private Dictionary<string, Dictionary<string, string>> _enums = new();

    /// <summary>Names of every enum available for resolveAs, sorted; empty until <see cref="Load"/>.</summary>
    public IReadOnlyList<string> EnumNames { get; private set; } = [];

    public void Load()
    {
        _enums = LoadEmbedded();
        EnumNames = _enums.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>True if <paramref name="resolveAs"/> targets a known enum in this store.</summary>
    public bool IsEnumResolve(string resolveAs)
        => resolveAs.StartsWith(Prefix, StringComparison.Ordinal)
           && _enums.ContainsKey(resolveAs[Prefix.Length..]);

    /// <summary>
    /// Resolves a raw integer value for a <c>enum:&lt;Name&gt;</c> resolveAs to its member name.
    /// Returns false when the resolveAs is not an enum target, the enum is unknown, or the value has
    /// no member (an out-of-range / newly added wire value).
    /// </summary>
    public bool TryResolve(string resolveAs, long value, out string member)
    {
        member = string.Empty;
        if (!resolveAs.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var name = resolveAs[Prefix.Length..];
        if (!_enums.TryGetValue(name, out var members)) return false;
        return members.TryGetValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture), out member!);
    }

    /// <summary>The display option strings for the param editor: <c>enum:&lt;Name&gt;</c> per enum.</summary>
    public IEnumerable<string> ResolveAsOptions() => EnumNames.Select(n => Prefix + n);

    private static Dictionary<string, Dictionary<string, string>> LoadEmbedded()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("resolve-enums.json", StringComparison.Ordinal));
            if (resName == null) return new();

            using var stream = asm.GetManifestResourceStream(resName)!;
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)
                   ?? new();
        }
        catch { return new(); }
    }
}
