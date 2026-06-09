using System.Text.Json;

namespace AlbionPacketExplorer.Models;

/// <summary>
/// A reference into a <see cref="PackedParamStore"/>: which chunk, the byte offset and length of a
/// packet's raw params-JSON slice, plus the param count (so callers can size/Count without decoding).
/// 16 bytes, a plain value type, so a <see cref="PacketEntry"/> holds it inline with no per-packet
/// heap object for its params.
/// </summary>
public readonly record struct ParamRef(int Chunk, int Offset, int Length, int Count)
{
    public static readonly ParamRef Empty = new(-1, 0, 0, 0);
    public bool IsEmpty => Chunk < 0;
}

/// <summary>
/// A chunked in-RAM byte arena that holds each packet's params as their raw UTF-8 JSON object bytes
/// (<c>{ "0": { "type": .., "value": .. }, .. }</c>) and decodes them lazily on demand.
///
/// <para>Why: at ~4M packets the eager per-packet <c>KeyValuePair&lt;string,ParamValue&gt;[]</c>
/// arrays (and their boxed/ref payloads) dominated live memory. Storing the compact source bytes and
/// re-parsing only when a row is actually viewed/filtered trades a little CPU on demand for a large
/// drop in retained memory.</para>
///
/// <para>Layout: a list of fixed-size chunks (16 MB) so the arena grows without a single giant LOH
/// array. A slice never straddles a chunk - if it does not fit the current chunk's remaining space a
/// new chunk starts; a slice larger than the chunk size gets its own oversized one-off chunk.</para>
///
/// <para>Threading: <see cref="Append"/> and chunk-list growth are guarded by a lock (appends happen
/// on the single background load thread, or batched on the UI thread during live capture).
/// <see cref="Decode"/> reads already-written, never-mutated bytes via a volatile snapshot of the
/// chunk array, so it runs lock-free on the UI and background filter threads.</para>
/// </summary>
public sealed class PackedParamStore
{
    private const int ChunkSize = 16 * 1024 * 1024; // 16 MB

    private readonly Lock _lock = new();

    // Volatile so Decode observes a consistent chunk-array reference after growth on another thread.
    private volatile byte[][] _chunks = [];
    private int _chunkCount;     // number of live chunks in _chunks
    private int _currentOffset;  // write cursor within the last chunk

    /// <summary>
    /// Copies a packet's params-JSON UTF-8 bytes into the arena and returns a <see cref="ParamRef"/>
    /// locating them. <paramref name="paramCount"/> is stored so callers can read the count without
    /// decoding. An empty span yields <see cref="ParamRef.Empty"/> (no bytes stored).
    /// </summary>
    public ParamRef Append(ReadOnlySpan<byte> paramJsonUtf8, int paramCount)
    {
        if (paramJsonUtf8.IsEmpty)
            return new ParamRef(-1, 0, 0, paramCount);

        lock (_lock)
        {
            // First write, or the slice does not fit the current chunk: start a fresh chunk. A slice
            // bigger than ChunkSize gets a dedicated oversized chunk sized exactly to it.
            if (_chunkCount == 0 || _currentOffset + paramJsonUtf8.Length > _chunks[_chunkCount - 1].Length)
            {
                int newChunkSize = Math.Max(ChunkSize, paramJsonUtf8.Length);
                AddChunk(newChunkSize);
                _currentOffset = 0;
            }

            int chunkIndex = _chunkCount - 1;
            int offset = _currentOffset;
            paramJsonUtf8.CopyTo(_chunks[chunkIndex].AsSpan(offset));
            _currentOffset += paramJsonUtf8.Length;

            return new ParamRef(chunkIndex, offset, paramJsonUtf8.Length, paramCount);
        }
    }

    // Grows the chunk array (geometric, like List<T>) and appends a fresh chunk. Caller holds _lock.
    private void AddChunk(int size)
    {
        if (_chunkCount == _chunks.Length)
        {
            int newCap = _chunks.Length == 0 ? 4 : _chunks.Length * 2;
            var grown = new byte[newCap][];
            System.Array.Copy(_chunks, grown, _chunkCount);
            _chunks = grown; // publish the grown reference (volatile) before exposing the new chunk
        }
        _chunks[_chunkCount] = new byte[size];
        _chunkCount++;
    }

    /// <summary>
    /// Decodes a stored slice back into a <see cref="ParamSet"/> using the shared
    /// <see cref="ParamCodec.ReadParams"/> - the exact same parser the file loader uses - so the
    /// result is value-identical to parsing the original line. Reads immutable, already-written bytes
    /// lock-free.
    /// </summary>
    public ParamSet Decode(ParamRef r)
    {
        if (r.IsEmpty || r.Length == 0)
            return ParamSet.Empty;

        var slice = _chunks[r.Chunk].AsSpan(r.Offset, r.Length);
        var reader = new Utf8JsonReader(slice);
        if (!reader.Read())
            return ParamSet.Empty;
        return ParamCodec.ReadParams(ref reader);
    }

    /// <summary>Drops every chunk so a dataset reset releases the whole arena to the GC.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _chunks = [];
            _chunkCount = 0;
            _currentOffset = 0;
        }
    }
}
