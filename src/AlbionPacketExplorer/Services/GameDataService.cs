using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public sealed class GameDataService
{
    private readonly string _indexedItemsPath;
    private Dictionary<int, (string UniqueName, string DisplayName)>? _items;

    public bool IsLoaded => _items != null;

    public GameDataService()
    {
        _indexedItemsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StatisticsAnalysisTool", "Instances", "3168FFFA", "GameFiles", "IndexedItems.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_indexedItemsPath)) return;

        try
        {
            await using var stream = File.OpenRead(_indexedItemsPath);
            var entries = await JsonSerializer.DeserializeAsync<List<IndexedItemEntry>>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (entries == null) return;

            var dict = new Dictionary<int, (string, string)>(entries.Count);
            foreach (var e in entries)
            {
                if (!int.TryParse(e.Index, out var idx)) continue;
                var display = e.LocalizedNames?.TryGetValue("EN-US", out var name) == true ? name : e.UniqueName;
                dict[idx] = (e.UniqueName, display ?? e.UniqueName);
            }
            _items = dict;
        }
        catch { }
    }

    public bool TryResolve(int index, out string uniqueName, out string displayName)
    {
        if (_items != null && _items.TryGetValue(index, out var entry))
        {
            uniqueName = entry.UniqueName;
            displayName = entry.DisplayName;
            return true;
        }
        uniqueName = displayName = string.Empty;
        return false;
    }

    private sealed class IndexedItemEntry
    {
        public string Index { get; set; } = string.Empty;
        public string UniqueName { get; set; } = string.Empty;
        public Dictionary<string, string>? LocalizedNames { get; set; }
    }
}
