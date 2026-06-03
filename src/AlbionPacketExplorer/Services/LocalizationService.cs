using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Loads per-culture string tables from <c>lang/&lt;culture&gt;.json</c> and resolves keys with a
/// requested -> English -> key fallback chain. String tables are embedded in the assembly; an
/// optional per-user override folder (AppData/AlbionPacketExplorer/lang) is layered on top so
/// translators can drop in or tweak a locale without rebuilding.
/// </summary>
public sealed class LocalizationService
{
    public static LocalizationService Instance { get; } = new();

    private const string FallbackCulture = "en";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _tables = new();
    private IReadOnlyDictionary<string, string> _current = new Dictionary<string, string>();
    private IReadOnlyDictionary<string, string> _fallback = new Dictionary<string, string>();

    /// <summary>Culture codes that have a string table available (embedded or override).</summary>
    public IReadOnlyList<string> Available { get; private set; } = [FallbackCulture];

    public string CurrentCulture { get; private set; } = FallbackCulture;

    /// <summary>Raised after the active culture changes so live bindings can refresh.</summary>
    public event Action? CultureChanged;

    private LocalizationService()
    {
        DiscoverCultures();
        _fallback = LoadTable(FallbackCulture);
        SetCulture(FallbackCulture);
    }

    /// <summary>Resolves a key for the active culture. Missing key -> English -> the key itself.</summary>
    public string this[string key] =>
        _current.TryGetValue(key, out var v) ? v :
        _fallback.TryGetValue(key, out var f) ? f :
        key;

    /// <summary>Convenience accessor for view-models: <c>Loc.T("nav.capture")</c>.</summary>
    public string T(string key) => this[key];

    /// <summary>Resolves a key whose value is a composite format string, then formats it.</summary>
    public string Format(string key, params object?[] args) =>
        string.Format(this[key], args);

    public void SetCulture(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) culture = FallbackCulture;
        _current = LoadTable(culture);
        CurrentCulture = culture;
        CultureChanged?.Invoke();
    }

    private IReadOnlyDictionary<string, string> LoadTable(string culture)
    {
        if (_tables.TryGetValue(culture, out var cached)) return cached;

        var table = new Dictionary<string, string>(ReadEmbedded(culture));
        foreach (var (k, v) in ReadOverride(culture)) // override wins over embedded
            table[k] = v;

        _tables[culture] = table;
        return table;
    }

    private static IReadOnlyDictionary<string, string> ReadEmbedded(string culture)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith($"lang.{culture}.json", StringComparison.OrdinalIgnoreCase));
            if (resName == null) return new Dictionary<string, string>();

            using var stream = asm.GetManifestResourceStream(resName)!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, JsonOpts)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static IReadOnlyDictionary<string, string> ReadOverride(string culture)
    {
        try
        {
            var path = Path.Combine(OverrideDir, $"{culture}.json");
            if (!File.Exists(path)) return new Dictionary<string, string>();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOpts)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void DiscoverCultures()
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { FallbackCulture };

        var asm = Assembly.GetExecutingAssembly();
        foreach (var n in asm.GetManifestResourceNames())
        {
            var c = CultureFromResourceName(n);
            if (c != null) found.Add(c);
        }

        try
        {
            if (Directory.Exists(OverrideDir))
                foreach (var f in Directory.EnumerateFiles(OverrideDir, "*.json"))
                    found.Add(Path.GetFileNameWithoutExtension(f));
        }
        catch { /* override dir optional */ }

        Available = [.. found];
    }

    private static string? CultureFromResourceName(string resourceName)
    {
        // Embedded names look like "AlbionPacketExplorer.lang.en.json"
        const string marker = ".lang.";
        var i = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (i < 0 || !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return null;
        var start = i + marker.Length;
        var culture = resourceName[start..^".json".Length];
        return culture.Contains('.') ? null : culture;
    }

    private static string OverrideDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer", "lang");
}
