using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlbionPacketExplorer.Services;

public record RowHidePreset(string Name, string PacketKey, IReadOnlyList<string> HiddenKeys);

/// <summary>
/// Persists which param keys are hidden per packet (kind:code) and named hide presets.
/// </summary>
public sealed class RowHideStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static string HiddenPath => AppPaths.RowHidden;

    private static string PresetsPath => AppPaths.RowHidePresets;

    // packetKey = "EVENT:32"
    private Dictionary<string, HashSet<string>> _hidden = [];
    private List<RowHidePreset> _presets = [];

    public IReadOnlyList<RowHidePreset> Presets => _presets;

    public void Load()
    {
        _hidden = LoadHidden();
        _presets = LoadPresets();
    }

    public IReadOnlySet<string> GetHidden(string packetKey)
        => _hidden.TryGetValue(packetKey, out var s) ? s : (IReadOnlySet<string>)new HashSet<string>();

    public void Hide(string packetKey, string key)
    {
        if (!_hidden.TryGetValue(packetKey, out var s))
            _hidden[packetKey] = s = [];
        s.Add(key);
        SaveHidden();
    }

    public void Unhide(string packetKey, string key)
    {
        if (_hidden.TryGetValue(packetKey, out var s))
        {
            s.Remove(key);
            if (s.Count == 0) _hidden.Remove(packetKey);
        }
        SaveHidden();
    }

    public void UnhideAll(string packetKey)
    {
        _hidden.Remove(packetKey);
        SaveHidden();
    }

    public void SavePreset(string name, string packetKey, IEnumerable<string> hiddenKeys)
    {
        var keys = hiddenKeys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        _presets.RemoveAll(p => p.Name == name);
        _presets.Add(new RowHidePreset(name, packetKey, keys));
        SavePresetsFile();
    }

    public void DeletePreset(string name)
    {
        _presets.RemoveAll(p => p.Name == name);
        SavePresetsFile();
    }

    public void ApplyPreset(string name, string packetKey)
    {
        var preset = _presets.FirstOrDefault(p => p.Name == name);
        if (preset == null) return;
        _hidden[packetKey] = [.. preset.HiddenKeys];
        SaveHidden();
    }

    private Dictionary<string, HashSet<string>> LoadHidden()
    {
        try
        {
            if (!File.Exists(HiddenPath)) return [];
            var json = File.ReadAllText(HiddenPath, System.Text.Encoding.UTF8);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node == null) return [];
            var result = new Dictionary<string, HashSet<string>>();
            foreach (var kv in node)
            {
                var arr = kv.Value?.AsArray();
                if (arr == null) continue;
                result[kv.Key] = [.. arr.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0)];
            }
            return result;
        }
        catch { return []; }
    }

    private void SaveHidden()
    {
        try
        {
            EnsureDir(HiddenPath);
            var node = new JsonObject();
            foreach (var kv in _hidden)
            {
                var arr = new JsonArray();
                foreach (var k in kv.Value.OrderBy(x => x, StringComparer.Ordinal))
                    arr.Add(k);
                node[kv.Key] = arr;
            }
            File.WriteAllText(HiddenPath, node.ToJsonString(JsonOpts), System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private List<RowHidePreset> LoadPresets()
    {
        try
        {
            if (!File.Exists(PresetsPath)) return [];
            var json = File.ReadAllText(PresetsPath, System.Text.Encoding.UTF8);
            var arr = JsonNode.Parse(json)?.AsArray();
            if (arr == null) return [];
            var result = new List<RowHidePreset>();
            foreach (var node in arr)
            {
                var name = node?["Name"]?.GetValue<string>() ?? "";
                var pk = node?["PacketKey"]?.GetValue<string>() ?? "";
                var keys = node?["HiddenKeys"]?.AsArray()
                    .Select(n => n?.GetValue<string>() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList() ?? [];
                if (!string.IsNullOrEmpty(name))
                    result.Add(new RowHidePreset(name, pk, keys));
            }
            return result;
        }
        catch { return []; }
    }

    private void SavePresetsFile()
    {
        try
        {
            EnsureDir(PresetsPath);
            File.WriteAllText(PresetsPath, JsonSerializer.Serialize(_presets, JsonOpts),
                System.Text.Encoding.UTF8);
        }
        catch { }
    }

    private static void EnsureDir(string path) =>
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
}
