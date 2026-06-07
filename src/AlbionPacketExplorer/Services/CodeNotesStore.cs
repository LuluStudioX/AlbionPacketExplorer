using System.Text.Json;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Persists a freeform note per packet code ("KIND:code"), so findings about an event/op type
/// survive across captures and reloads.
/// </summary>
public sealed class CodeNotesStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static string FilePath => AppPaths.CodeNotes;

    private Dictionary<string, string> _notes = [];

    public void Load() => _notes = LoadFile();

    public string Get(string kind, int code)
        => _notes.TryGetValue($"{kind}:{code}", out var v) ? v : string.Empty;

    public void Set(string kind, int code, string? note)
    {
        var k = $"{kind}:{code}";
        if (string.IsNullOrWhiteSpace(note)) _notes.Remove(k);
        else _notes[k] = note;
        Save();
    }

    public bool Has(string kind, int code) => _notes.ContainsKey($"{kind}:{code}");

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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_notes, JsonOpts));
        }
        catch { /* best effort */ }
    }
}
