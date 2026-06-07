using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Persists user-assigned labels for specific field values, e.g. EVENT 55 key 2 value "2" =
/// "Upgrade". Lets enum-like fields read meaningfully across the app. Key format
/// "KIND:code:byteKey:value".
/// </summary>
public sealed class EnumLabelStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static string FilePath => AppPaths.EnumLabels;

    private Dictionary<string, string> _labels = [];

    public void Load() => _labels = LoadFile();

    public string? Get(string kind, int code, string key, string value)
        => _labels.TryGetValue(MakeKey(kind, code, key, value), out var v) ? v : null;

    public void Set(string kind, int code, string key, string value, string? label)
    {
        var k = MakeKey(kind, code, key, value);
        if (string.IsNullOrWhiteSpace(label)) _labels.Remove(k);
        else _labels[k] = label.Trim();
        Save();
    }

    private static string MakeKey(string kind, int code, string key, string value)
        => $"{kind}:{code}:{key}:{value}";

    private static Dictionary<string, string> LoadFile()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch { /* fall back to empty */ }
        return [];
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_labels, JsonOpts));
        }
        catch { /* best effort */ }
    }
}
