using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public record FilterPreset(string Name, string Kind, string Code, string EventName, string Params);

public record FilterState(string Kind = "", string Code = "", string EventName = "", string Params = "");

public sealed class FilterPresetStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string PresetsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer", "filter-presets.json");

    private static string LastFilterPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer", "filter-last.json");

    public static List<FilterPreset> LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetsPath)) return [];
            var json = File.ReadAllText(PresetsPath, System.Text.Encoding.UTF8);
            return JsonSerializer.Deserialize<List<FilterPreset>>(json, JsonOpts) ?? [];
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
            return JsonSerializer.Deserialize<FilterState>(json, JsonOpts) ?? new FilterState();
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

    private static void EnsureDir(string path) =>
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
}
