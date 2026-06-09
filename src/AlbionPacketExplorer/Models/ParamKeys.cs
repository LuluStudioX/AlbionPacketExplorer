namespace AlbionPacketExplorer.Models;

/// <summary>
/// Shared, pre-allocated cache of the 256 byte-index key strings ("0".."255") that every packet's
/// params are keyed by. Reusing these collapses ~16M freshly-allocated key strings down to 256
/// shared instances. Any key outside 0..255 (never expected on the wire) falls back to its own
/// string and is interned so identical out-of-range keys still dedupe.
/// </summary>
public static class ParamKeys
{
    private static readonly string[] Cache = BuildCache();

    private static string[] BuildCache()
    {
        var cache = new string[256];
        for (int i = 0; i < cache.Length; i++)
            cache[i] = string.Intern(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return cache;
    }

    /// <summary>The shared instance for a byte index (0..255).</summary>
    public static string Get(int index) => Cache[index];

    /// <summary>
    /// Returns the shared instance for a key string that parses to 0..255; otherwise interns and
    /// returns the original so even unexpected keys dedupe across packets.
    /// </summary>
    public static string Intern(string key) =>
        int.TryParse(key, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n)
            && n is >= 0 and <= 255
                ? Cache[n]
                : string.Intern(key);
}
