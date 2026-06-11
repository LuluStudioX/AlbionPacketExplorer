using System.Text;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Disk-backed store for captured raw payloads (base64, one per line - the exact .b64 save format).
/// Live capture used to hoard every payload as a byte[] in memory, which grew to multiple GB over a
/// long session; spilling to a temp file keeps Save-as-RAW working with O(1) memory. The spill file
/// is opened with DeleteOnClose so it is removed on Clear/Dispose or process exit on every OS, and
/// FileShare.Read so SaveTo can copy it while it stays open.
/// </summary>
public sealed class RawSpillStore : IDisposable
{
    private readonly Lock _gate = new();
    private string? _path;
    private StreamWriter? _writer;
    private int _count;

    public int Count { get { lock (_gate) return _count; } }
    public bool HasAny => Count > 0;

    /// <summary>Appends one payload as a base64 line. Safe from any thread (capture or UI).</summary>
    public void Append(byte[] payload)
    {
        lock (_gate)
        {
            _writer ??= Open();
            _writer.WriteLine(Convert.ToBase64String(payload));
            _count++;
        }
    }

    private StreamWriter Open()
    {
        _path = Path.Combine(Path.GetTempPath(), $"apx-raw-{Guid.NewGuid():N}.b64");
        var stream = new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
            bufferSize: 1 << 16, FileOptions.DeleteOnClose);
        return new StreamWriter(stream, Encoding.ASCII);
    }

    /// <summary>Flushes and copies the spill file to <paramref name="destination"/> (overwrites).</summary>
    public void SaveTo(string destination)
    {
        lock (_gate)
        {
            if (_writer == null || _path == null)
            {
                File.WriteAllText(destination, "", Encoding.ASCII);
                return;
            }
            _writer.Flush();
            File.Copy(_path, destination, overwrite: true);
        }
    }

    /// <summary>Drops all spilled payloads; DeleteOnClose removes the file with the writer.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
            _path = null;
            _count = 0;
        }
    }

    public void Dispose() => Clear();
}
