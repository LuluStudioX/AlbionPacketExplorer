using System.Buffers;
using System.IO.MemoryMappedFiles;
using System.Text.Json;
using System.Threading;

namespace AlbionPacketExplorer.Models;

/// <summary>
/// A reference into a <see cref="PackedParamStore"/>: the byte offset and length of a packet's raw
/// params-JSON slice in the spill file, plus the param count (so callers can size/Count without
/// decoding). A plain value type, so a <see cref="PacketEntry"/> holds it inline with no per-packet
/// heap object for its params.
/// </summary>
public readonly record struct ParamRef(long Offset, int Length, int Count)
{
    public static readonly ParamRef Empty = new(-1, 0, 0);
    public bool IsEmpty => Offset < 0;
}

/// <summary>
/// A byte arena that holds each packet's params as their raw UTF-8 JSON object bytes
/// (<c>{ "0": { "type": .., "value": .. }, .. }</c>) and decodes them lazily on demand. The bytes
/// live in a temporary memory-mapped FILE under the OS temp dir, NOT on the managed heap, so the
/// arena's pages are file-backed and reclaimable by the OS - committed/private memory does not grow
/// with the arena the way a managed <c>byte[]</c> chunk list does.
///
/// <para>Why: at ~4M packets the params bytes alone are ~1.8 GB. As managed LOH chunks that whole
/// arena counts against private/committed memory and is never returned to the OS. Spilling it to a
/// mmap file turns those bytes into clean file-backed pages the OS can evict under pressure, cutting
/// the process's retained set substantially while keeping decode O(copy a few hundred bytes).</para>
///
/// <para>File: a temp file <c>apx-params-{guid}.bin</c> opened with
/// <see cref="FileOptions.DeleteOnClose"/> so it is removed on dispose or process exit on every OS.
/// The map is created with <c>mapName: null</c> - named maps are Windows-only; a null name is
/// file-path-backed and works on Windows, Linux and macOS.</para>
///
/// <para>Threading: a <see cref="ReaderWriterLockSlim"/>. <see cref="Append"/> and the grow that
/// recreates the map take the WRITE lock (appends happen on the single background load thread XOR the
/// batched live-capture path - never both at once). <see cref="Decode"/> takes the READ lock and
/// copies the small slice out before parsing, so it stays correct against a concurrent grow that
/// disposes and recreates the accessor.</para>
/// </summary>
public sealed class PackedParamStore : IDisposable
{
    // Start at 256 MB and double on overflow. A typical 3.9M-packet file spills ~1.8 GB, so growth
    // runs a handful of times total (256 MB -> 512 MB -> 1 GB -> 2 GB), each grow O(1) work.
    private const long InitialCapacity = 256L * 1024 * 1024;

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    private readonly string _path;
    private FileStream _file;
    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;
    private long _capacity;
    private long _cursor;
    private bool _disposed;

    public PackedParamStore()
    {
        _path = Path.Combine(Path.GetTempPath(), $"apx-params-{Guid.NewGuid():N}.bin");
        _capacity = InitialCapacity;
        // DeleteOnClose: the temp file is removed when the FileStream closes (dispose) or the process
        // exits - cross-platform, no leak on crash. We own the stream and pass leaveOpen: true to the
        // map so disposing the mmf during a grow does NOT close it; a grow re-wraps the SAME open file
        // handle at a larger capacity. The stream is closed explicitly in DisposeMap/Dispose.
        _file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 4096, FileOptions.DeleteOnClose);
        (_mmf, _accessor) = CreateMap(_file, _capacity);
    }

    // Wraps the given (already open) FileStream in a mmf + view accessor at the requested capacity.
    // mapName MUST be null for cross-platform; leaveOpen: true so disposing the mmf during a grow
    // does NOT close our FileStream - we reuse the same open file handle for the larger map.
    private static (MemoryMappedFile, MemoryMappedViewAccessor) CreateMap(FileStream file, long capacity)
    {
        var mmf = MemoryMappedFile.CreateFromFile(
            file,
            mapName: null,
            capacity,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true);
        var accessor = mmf.CreateViewAccessor(0, capacity, MemoryMappedFileAccess.ReadWrite);
        return (mmf, accessor);
    }

    /// <summary>
    /// Copies a packet's params-JSON UTF-8 bytes into the spill file and returns a
    /// <see cref="ParamRef"/> locating them. <paramref name="paramCount"/> is stored so callers can
    /// read the count without decoding. An empty span yields an empty ref (no bytes stored).
    /// </summary>
    public ParamRef Append(ReadOnlySpan<byte> paramJsonUtf8, int paramCount)
    {
        if (paramJsonUtf8.IsEmpty)
            return new ParamRef(-1, 0, paramCount);

        int len = paramJsonUtf8.Length;

        // WriteArray needs a byte[]; copy the span into a pooled buffer (Span can't be captured past
        // the accessor call boundary cleanly, and a pooled array avoids a per-append allocation).
        byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
        paramJsonUtf8.CopyTo(buffer);

        _lock.EnterWriteLock();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PackedParamStore));

            if (_cursor + len > _capacity)
                Grow(_cursor + len);

            long offset = _cursor;
            _accessor.WriteArray(offset, buffer, 0, len);
            _cursor += len;
            return new ParamRef(offset, len, paramCount);
        }
        finally
        {
            _lock.ExitWriteLock();
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Doubles capacity until it fits the required total, then recreates the map over the SAME open
    // FileStream at the new size. Runs under the write lock so no Decode observes a disposed accessor.
    private void Grow(long required)
    {
        long newCapacity = _capacity;
        while (newCapacity < required)
            newCapacity *= 2;

        _accessor.Dispose();
        _mmf.Dispose(); // leaveOpen:true kept _file open; the larger map re-wraps that same handle
        (_mmf, _accessor) = CreateMap(_file, newCapacity);
        _capacity = newCapacity;
    }

    /// <summary>
    /// Decodes a stored slice back into a <see cref="ParamSet"/> using the shared
    /// <see cref="ParamCodec.ReadParams"/> - the exact same parser the file loader uses - so the
    /// result is value-identical to parsing the original line. Reads immutable, already-written bytes.
    /// </summary>
    public ParamSet Decode(ParamRef r)
    {
        if (r.IsEmpty || r.Length == 0)
            return ParamSet.Empty;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(r.Length);
        try
        {
            _lock.EnterReadLock();
            try
            {
                if (_disposed) return ParamSet.Empty;
                _accessor.ReadArray(r.Offset, buffer, 0, r.Length);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            var reader = new Utf8JsonReader(buffer.AsSpan(0, r.Length));
            if (!reader.Read())
                return ParamSet.Empty;
            return ParamCodec.ReadParams(ref reader);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Resets to a fresh empty spill file so a new dataset starts clean and the previous dataset's
    /// bytes are released to the OS immediately. The old file is deleted (DeleteOnClose) when its
    /// stream closes here.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_disposed) return;
            DisposeMap();

            _capacity = InitialCapacity;
            _cursor = 0;
            // Reuse the same path: the previous file is already deleted by DisposeMap's stream close,
            // so CreateNew on the same name succeeds.
            _file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                bufferSize: 4096, FileOptions.DeleteOnClose);
            (_mmf, _accessor) = CreateMap(_file, _capacity);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    // Disposes accessor + mmf + stream. DeleteOnClose removes the temp file when the stream closes.
    private void DisposeMap()
    {
        _accessor.Dispose();
        _mmf.Dispose();
        _file.Dispose();
    }

    public void Dispose()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_disposed) return;
            _disposed = true;
            DisposeMap();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        _lock.Dispose();
    }
}
