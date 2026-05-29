using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public sealed class GameDataService
{
    private const string RemoteUrl = "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/items.json";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AlbionPacketExplorer", "items.json");

    private Dictionary<int, (string UniqueName, string DisplayName)>? _items;

    public bool IsLoaded => _items != null;

    public async Task LoadAsync(Action<string>? statusCallback = null)
    {
        var cacheFile = new FileInfo(CachePath);
        bool needsFetch = !cacheFile.Exists || DateTime.UtcNow - cacheFile.LastWriteTimeUtc > CacheMaxAge;

        if (needsFetch)
        {
            statusCallback?.Invoke("Downloading item data from ao-bin-dumps…");
            await TryFetchAndCacheAsync(statusCallback);
        }

        if (File.Exists(CachePath))
        {
            statusCallback?.Invoke("Loading item data…");
            await LoadFromFileAsync(CachePath);
        }

        if (IsLoaded)
            statusCallback?.Invoke($"Item data ready ({_items!.Count:N0} items).");
        else
            statusCallback?.Invoke("Item data unavailable. Resolve feature disabled.");
    }

    private async Task TryFetchAndCacheAsync(Action<string>? statusCallback)
    {
        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            var bytes = await http.GetByteArrayAsync(RemoteUrl);

            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            await File.WriteAllBytesAsync(CachePath, bytes);
        }
        catch (Exception ex)
        {
            statusCallback?.Invoke($"Item data fetch failed ({ex.Message}). Using cached data if available.");
        }
    }

    private async Task LoadFromFileAsync(string path)
    {
        try
        {
            await using var stream = File.OpenRead(path);
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
