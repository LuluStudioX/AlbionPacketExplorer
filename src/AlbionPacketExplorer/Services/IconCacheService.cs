using Avalonia.Media.Imaging;
using System.Collections.Concurrent;

namespace AlbionPacketExplorer.Services;

public sealed class IconCacheService : IDisposable
{
    private const string RenderBaseUrl = "https://render.albiononline.com/v1/item/";
    private const int IconSize = 64;
    private const int MemoryCacheLimit = 500;

    private static string DiskCacheDir => AppPaths.IconCacheDir;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ConcurrentDictionary<string, Bitmap?> _memCache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private readonly LinkedList<string> _lruOrder = new();
    private readonly object _lruLock = new();

    public void Dispose() => _http.Dispose();

    public async Task<Bitmap?> GetIconAsync(string uniqueName)
    {
        if (string.IsNullOrEmpty(uniqueName)) return null;

        if (_memCache.TryGetValue(uniqueName, out var cached))
        {
            TouchLru(uniqueName);
            return cached;
        }

        var sem = _locks.GetOrAdd(uniqueName, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        try
        {
            // double-check after acquiring lock
            if (_memCache.TryGetValue(uniqueName, out cached)) return cached;

            var bitmap = await LoadOrFetchAsync(uniqueName);
            Store(uniqueName, bitmap);
            return bitmap;
        }
        finally
        {
            sem.Release();
        }
    }

    private async Task<Bitmap?> LoadOrFetchAsync(string uniqueName)
    {
        var diskPath = DiskPath(uniqueName);
        if (File.Exists(diskPath))
            return LoadBitmap(diskPath);

        return await FetchAndSaveAsync(uniqueName, diskPath);
    }

    private async Task<Bitmap?> FetchAndSaveAsync(string uniqueName, string diskPath)
    {
        try
        {
            Directory.CreateDirectory(DiskCacheDir);
            var url = $"{RenderBaseUrl}{Uri.EscapeDataString(uniqueName)}.png?size={IconSize}";
            var bytes = await _http.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(diskPath, bytes);
            return LoadBitmap(diskPath);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try { return new Bitmap(path); }
        catch { return null; }
    }

    private void Store(string uniqueName, Bitmap? bitmap)
    {
        _memCache[uniqueName] = bitmap;
        lock (_lruLock)
        {
            _lruOrder.AddLast(uniqueName);
            if (_lruOrder.Count > MemoryCacheLimit)
            {
                var oldest = _lruOrder.First!.Value;
                _lruOrder.RemoveFirst();
                if (_memCache.TryRemove(oldest, out var evicted))
                    evicted?.Dispose();
            }
        }
    }

    private void TouchLru(string uniqueName)
    {
        lock (_lruLock)
        {
            var node = _lruOrder.Find(uniqueName);
            if (node != null)
            {
                _lruOrder.Remove(node);
                _lruOrder.AddLast(uniqueName);
            }
        }
    }

    private static string DiskPath(string uniqueName) =>
        Path.Combine(DiskCacheDir, $"{uniqueName}.png");
}
