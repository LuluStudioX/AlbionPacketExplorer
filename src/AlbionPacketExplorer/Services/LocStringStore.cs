using System.Reflection;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Maps Albion localization keys (the <c>@SOMETHING</c> strings that appear as string-typed packet
/// params) to their English text, from the embedded <c>Assets/loc-strings.json</c> (sourced from the
/// community <c>ao-data/ao-bin-dumps</c> localization dump). Lets a packet param showing
/// <c>@PARTYFINDER_JOINREQUEST_DECLINED</c> read as "{0} has declined your party join request!".
/// </summary>
public sealed class LocStringStore
{
    // key (with leading '@') -> English text
    private Dictionary<string, string> _loc = new();

    public bool IsLoaded => _loc.Count > 0;

    public void Load() => _loc = LoadEmbedded();

    /// <summary>Resolves a leading-<c>@</c> localization key to its English text; false otherwise.</summary>
    public bool TryResolve(string value, out string text)
    {
        text = string.Empty;
        if (string.IsNullOrEmpty(value) || value[0] != '@') return false;
        return _loc.TryGetValue(value, out text!);
    }

    private static Dictionary<string, string> LoadEmbedded()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("loc-strings.json", StringComparison.Ordinal));
            if (resName == null) return new();

            using var stream = asm.GetManifestResourceStream(resName)!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                   ?? new();
        }
        catch { return new(); }
    }
}
