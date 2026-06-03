using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlbionPacketExplorer.Services;

public record FilterPreset(string Name, string Query);

public record FilterState(string Query = "");

public sealed class FilterPresetStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string PresetsPath => AppPaths.FilterPresets;

    private static string LastFilterPath => AppPaths.LastFilter;

    public static List<FilterPreset> LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetsPath)) return [];
            var json = File.ReadAllText(PresetsPath, System.Text.Encoding.UTF8);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return [];

            var result = new List<FilterPreset>();
            foreach (var node in arr)
            {
                var name = node?["Name"]?.GetValue<string>() ?? "";
                var query = node?["Query"]?.GetValue<string>();
                if (query == null)
                    query = MigrateOldFields(node);
                result.Add(new FilterPreset(name, query));
            }
            return result;
        }
        catch { return []; }
    }

    public static void SavePresets(IEnumerable<FilterPreset> presets)
    {
        try
        {
            EnsureDir(PresetsPath);
            File.WriteAllText(PresetsPath, JsonSerializer.Serialize(presets.ToList(), JsonOpts),
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    public static FilterState LoadLastFilter()
    {
        try
        {
            if (!File.Exists(LastFilterPath)) return new FilterState();
            var json = File.ReadAllText(LastFilterPath, System.Text.Encoding.UTF8);
            var node = JsonNode.Parse(json);
            var query = node?["Query"]?.GetValue<string>();
            if (query == null)
                query = MigrateOldFields(node);
            return new FilterState(query);
        }
        catch { return new FilterState(); }
    }

    public static void SaveLastFilter(FilterState state)
    {
        try
        {
            EnsureDir(LastFilterPath);
            File.WriteAllText(LastFilterPath, JsonSerializer.Serialize(state, JsonOpts),
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    // Reconstruct a unified query from old Kind/Code/EventName/Params fields.
    // Old Code field used plain codes as exclusions/inclusions.
    // Old EventName field used FilterHelper token syntax (exclusions/inclusions on name column).
    // Old Kind field was substring on kind column.
    // Old Params field was substring on params column.
    private static string MigrateOldFields(JsonNode? node)
    {
        var parts = new List<string>();

        var kind = node?["Kind"]?.GetValue<string>() ?? "";
        var code = node?["Code"]?.GetValue<string>() ?? "";
        var name = node?["EventName"]?.GetValue<string>() ?? "";
        var parms = node?["Params"]?.GetValue<string>() ?? "";

        // Kind: plain tokens → prefix with kind:
        foreach (var t in Tokenize(kind))
            parts.Add(t.StartsWith('-') ? $"-kind:{t[1..]}" : $"kind:{t}");

        // Code: tokens were already plain code numbers with optional - prefix
        foreach (var t in Tokenize(code))
            parts.Add(t); // already in new syntax: -32 or 32 → code match by exact

        // EventName: tokens → prefix with name:
        foreach (var t in Tokenize(name))
            parts.Add(t.StartsWith('-') ? $"-name:{t[1..]}" : $"name:{t}");

        // Params: tokens → prefix with params:
        foreach (var t in Tokenize(parms))
            parts.Add(t.StartsWith('-') ? $"-params:{t[1..]}" : $"params:{t}");

        return string.Join(" ", parts);
    }

    private static IEnumerable<string> Tokenize(string s)
        => s.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

    private static void EnsureDir(string path) =>
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
}
