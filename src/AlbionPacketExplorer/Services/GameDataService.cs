using System.Text.Json;

namespace AlbionPacketExplorer.Services;

public sealed class GameDataService
{
    private const string RemoteUrl = "https://raw.githubusercontent.com/ao-data/ao-bin-dumps/master/formatted/items.json";
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(7);

    private static string CachePath => AppPaths.ItemCache;

    private Dictionary<int, (string UniqueName, string DisplayName)>? _items;
    private Dictionary<string, string>? _displayByUniqueName;

    public bool IsLoaded => _items != null;

    /// <summary>Number of items in the loaded data set.</summary>
    public int ItemCount => _items?.Count ?? 0;

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
            var byName = new Dictionary<string, string>(entries.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                var display = e.LocalizedNames?.TryGetValue("EN-US", out var name) == true ? name : e.UniqueName;
                var displayName = display ?? e.UniqueName;
                if (int.TryParse(e.Index, out var idx))
                    dict[idx] = (e.UniqueName, displayName);
                if (!string.IsNullOrEmpty(e.UniqueName))
                    byName[e.UniqueName] = displayName;
            }
            _items = dict;
            _displayByUniqueName = byName;
        }
        catch { }
    }

    public bool TryResolveByUniqueName(string uniqueName, out string displayName)
    {
        if (_displayByUniqueName != null && _displayByUniqueName.TryGetValue(uniqueName, out var d))
        {
            displayName = d;
            return true;
        }
        displayName = string.Empty;
        return false;
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
