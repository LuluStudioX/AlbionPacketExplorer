using System.Buffers;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace AlbionPacketExplorer.Models;

/// <summary>
/// A reference into a <see cref="PackedParamStore"/>: the byte offset and length of a packet's
/// COMPACT-BINARY params slice in the spill file, plus the param count (so callers can size/Count
/// without decoding). A plain value type, so a <see cref="PacketEntry"/> holds it inline with no
/// per-packet heap object for its params.
/// </summary>
public readonly record struct ParamRef(long Offset, int Length, int Count)
{
    public static readonly ParamRef Empty = new(-1, 0, 0);
    public bool IsEmpty => Offset < 0;
}

/// <summary>
/// A byte arena that holds each packet's params as a COMPACT BINARY encoding (NOT JSON text) and
/// decodes them lazily on demand. The bytes live in a temporary memory-mapped FILE under the OS temp
/// dir, NOT on the managed heap, so the arena's pages are file-backed and reclaimable by the OS -
/// committed/private memory does not grow with the arena the way a managed <c>byte[]</c> chunk list
/// does.
///
/// <para>Why binary: the previous arena stored each packet's params as raw param-JSON UTF-8
/// (<c>{ "0": { "type": .., "value": .. }, .. }</c>) - ~1.8 GB at ~4M packets, and every scroll/filter
/// decode re-parsed JSON text. The binary form drops the JSON structural overhead (key quoting,
/// <c>"type"</c>/<c>"value"</c> literals, decimal number text) and de-duplicates the ~15 distinct
/// type-name strings into a per-store id table, cutting the file to roughly 0.6-0.9 GB and making
/// decode a straight binary read (no tokenizer, no number text parsing) on the scroll/filter hot
/// path.</para>
///
/// <para>Format (little-endian, see <see cref="EncodeBinary"/>/<see cref="DecodeBinary"/>):
/// per packet, for each param in insertion order: 1 byte key (0-255), a varint TYPE-ID into the
/// store's type table, then a tagged VALUE (1-byte kind + payload). The type table maps id -&gt;
/// interned type string and lives for the store's lifetime (it resets on <see cref="Clear"/> with the
/// arena, so ids are always self-consistent within one dataset).</para>
///
/// <para>File: a temp file <c>apx-params-{guid}.bin</c> opened with
/// <see cref="FileOptions.DeleteOnClose"/> so it is removed on dispose or process exit on every OS.
/// The map is created with <c>mapName: null</c> - named maps are Windows-only; a null name is
/// file-path-backed and works on Windows, Linux and macOS.</para>
///
/// <para>Threading: a <see cref="ReaderWriterLockSlim"/>. <see cref="Append"/> and the grow that
/// recreates the map take the WRITE lock (appends happen on the single background load thread XOR the
/// batched live-capture path - never both at once), so the type table is only mutated under that same
/// write lock. <see cref="Decode"/> takes the READ lock and copies the small slice out before
/// decoding, so it stays correct against a concurrent grow that disposes and recreates the
/// accessor.</para>
/// </summary>
public sealed class PackedParamStore : IDisposable
{
    // Start at 256 MB and double on overflow. A binary 3.9M-packet file spills ~0.6-0.9 GB, so growth
    // runs a few times total (256 MB -> 512 MB -> 1 GB), each grow O(1) work.
    private const long InitialCapacity = 256L * 1024 * 1024;

    // Value-kind tags (1 byte, written before each value payload). Recursion uses the same tags.
    private const byte KindNull = 0;   // no payload
    private const byte KindLong = 1;   // 8 bytes LE
    private const byte KindDouble = 2; // 8 bytes LE (DoubleToInt64Bits)
    private const byte KindBool = 3;   // 1 byte (0/1)
    private const byte KindString = 4; // varint byte-length + UTF-8
    private const byte KindArray = 5;  // varint count + each element as a tagged value
    private const byte KindDict = 6;   // varint count + each: varint keylen + UTF-8 key, then tagged value

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);

    private readonly string _path;
    private FileStream _file;
    private MemoryMappedFile _mmf;
    private MemoryMappedViewAccessor _accessor;
    private long _capacity;
    private long _cursor;
    private bool _disposed;

    // Per-store TYPE-ID table: distinct ParamValue.Type strings -> small int id. Each distinct type
    // name (~15: Int64, Int16, Byte, Single, Double, Boolean, String, Byte[], Int32[], ...) is written
    // once as a varint id; decode maps the id back to the interned string. This is the big compaction
    // win over repeating the type text per param.
    //
    // CONCURRENT: parse workers encode batches in parallel (BatchEncoder), so lookups must be
    // lock-free - a ConcurrentDictionary for name -> id and a volatile copy-on-write array for
    // id -> name. Registering a NEW name (rare: ~15 per dataset lifetime) takes _typeRegLock and
    // publishes the grown array BEFORE the id becomes discoverable, so any id a worker can write
    // into a blob is always resolvable by a concurrent Decode. Resets with the arena on Clear.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _typeIds = new(StringComparer.Ordinal);
    private volatile string[] _typeNames = [];
    private readonly object _typeRegLock = new();

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

    // Raw span copies through the view's base pointer. MemoryMappedViewAccessor.WriteArray/ReadArray
    // marshal element-by-element and were ~6s of a 4M-packet load; a direct memcpy is ~10x faster.
    // Callers already hold the appropriate lock, and _accessor only swaps under the write lock (Grow),
    // so the pointer acquired here stays valid for the copy.
    private unsafe void WriteBytes(long offset, byte[] src, int srcOffset, int length)
    {
        if (length <= 0) return;
        var handle = _accessor.SafeMemoryMappedViewHandle;
        byte* basePtr = null;
        handle.AcquirePointer(ref basePtr);
        try
        {
            var dest = new Span<byte>(basePtr + _accessor.PointerOffset + offset, length);
            new ReadOnlySpan<byte>(src, srcOffset, length).CopyTo(dest);
        }
        finally
        {
            handle.ReleasePointer();
        }
    }

    private unsafe void ReadBytes(long offset, byte[] dest, int destOffset, int length)
    {
        if (length <= 0) return;
        var handle = _accessor.SafeMemoryMappedViewHandle;
        byte* basePtr = null;
        handle.AcquirePointer(ref basePtr);
        try
        {
            var src = new ReadOnlySpan<byte>(basePtr + _accessor.PointerOffset + offset, length);
            src.CopyTo(new Span<byte>(dest, destOffset, length));
        }
        finally
        {
            handle.ReleasePointer();
        }
    }

    /// <summary>
    /// Binary-encodes a packet's params into the spill file and returns a <see cref="ParamRef"/>
    /// locating the bytes. The param count is stored in the ref so callers can read the count without
    /// decoding. An empty set yields an empty ref (no bytes stored). The encode (and any new type-id)
    /// happens under the write lock, so the type table stays consistent with the appended bytes.
    /// </summary>
    public ParamRef Append(ParamSet ps)
    {
        int count = ps.Count;
        if (count == 0)
            return new ParamRef(-1, 0, 0);

        // Encode into a pooled, growable scratch writer, then copy the written span into the mmap. The
        // type table is read/extended during encode, so the whole encode runs under the write lock.
        _lock.EnterWriteLock();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PackedParamStore));

            var writer = new BinaryWriterScratch();
            try
            {
                EncodeBinary(ps, ref writer);
                int len = writer.Length;

                if (_cursor + len > _capacity)
                    Grow(_cursor + len);

                long offset = _cursor;
                WriteBytes(offset, writer.Buffer, 0, len);
                _cursor += len;
                return new ParamRef(offset, len, count);
            }
            finally
            {
                writer.Dispose();
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Appends a worker-encoded params blob (built by a <see cref="BatchEncoder"/>) in ONE cursor
    /// bump and ONE write, and returns the arena base offset the encoder's blob-relative refs get
    /// rebased against. The byte layout is identical to per-packet <see cref="Append"/> calls in the
    /// same order, so Decode is oblivious to which path stored the bytes.
    /// </summary>
    public long AppendEncodedBatch(byte[] blob, int length)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PackedParamStore));

            if (_cursor + length > _capacity)
                Grow(_cursor + length);

            long offset = _cursor;
            WriteBytes(offset, blob, 0, length);
            _cursor += length;
            return offset;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Grows the arena so it can hold at least <paramref name="capacity"/> bytes without any further
    /// growth. Call once before a parallel load (pass the source file size; the binary param encoding
    /// is always smaller than its JSON, so the blobs are guaranteed to fit). With no mid-load grow,
    /// <see cref="ReserveAndWriteConcurrent"/> needs no lock and the view accessor never swaps under
    /// the concurrent writers.
    /// </summary>
    public void Reserve(long capacity)
    {
        if (capacity <= 0) return;
        _lock.EnterWriteLock();
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PackedParamStore));
            if (capacity > _capacity) Grow(capacity);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Lock-free batch append for the parallel load path: bumps the cursor atomically and writes the
    /// blob straight into its reserved (disjoint) region of the arena, so every parse worker writes
    /// its own params concurrently with no serialization. REQUIRES a prior <see cref="Reserve"/> sized
    /// to the whole dataset - the regions are pre-reserved, so this never grows (a grow would swap the
    /// view out from under the other writers). The byte layout is identical to <see cref="Append"/>.
    /// </summary>
    public long ReserveAndWriteConcurrent(byte[] blob, int length)
    {
        if (length <= 0)
            return System.Threading.Interlocked.Read(ref _cursor);

        long offset = System.Threading.Interlocked.Add(ref _cursor, length) - length;
        if (offset + length > _capacity)
            throw new InvalidOperationException(
                "PackedParamStore overflow: call Reserve(fileBytes) before a concurrent load.");

        WriteBytes(offset, blob, 0, length);
        return offset;
    }

    /// <summary>
    /// A per-batch params encoder for parse workers: encodes ParamSets back-to-back into one pooled
    /// blob OFF the store's write lock (the type registry is concurrent), returning BLOB-RELATIVE
    /// refs. The worker writes the whole blob via <see cref="ReserveAndWriteConcurrent"/> and rebases
    /// the refs by the returned base offset. Encoding is the exact code path <see cref="Append"/>
    /// uses, so the bytes are identical.
    /// </summary>
    public sealed class BatchEncoder
    {
        private readonly PackedParamStore _store;
        private byte[] _buffer;
        private int _length;

        public BatchEncoder(PackedParamStore store)
        {
            _store = store;
            _buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            _length = 0;
        }

        /// <summary>Encodes one packet's params; returns a blob-relative ref (empty set: empty ref).</summary>
        public ParamRef Add(ParamSet ps)
        {
            int count = ps.Count;
            if (count == 0)
                return ParamRef.Empty;

            int start = _length;
            var w = new BinaryWriterScratch(_buffer, _length);
            _store.EncodeBinary(ps, ref w);
            _buffer = w.Buffer; // the writer may have grown (swapped) the pooled array
            _length = w.Length;
            return new ParamRef(start, _length - start, count);
        }

        /// <summary>
        /// Hands the blob to the caller (who must return it to <see cref="ArrayPool{T}.Shared"/>
        /// after writing it to the store) and neutralizes this encoder.
        /// </summary>
        public byte[] Detach(out int length)
        {
            length = _length;
            byte[] blob = _buffer;
            _buffer = [];
            _length = 0;
            return blob;
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
    /// Binary-decodes a stored slice back into a <see cref="ParamSet"/> value-identical to the set
    /// that was appended (same keys/order, same <c>Type</c>, same <c>Value</c> shape). Reads
    /// immutable, already-written bytes; the type table is only read here.
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
                ReadBytes(r.Offset, buffer, 0, r.Length);
                // Decode while holding the read lock: the type table is shared, and a concurrent
                // single-writer Append may extend it; reading under the lock keeps it consistent.
                return DecodeBinary(buffer.AsSpan(0, r.Length), r.Count);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    /// Resets to a fresh empty spill file so a new dataset starts clean and the previous dataset's
    /// bytes are released to the OS immediately. The old file is deleted (DeleteOnClose) when its
    /// stream closes here. The type table resets too, so ids stay self-consistent per dataset.
    /// </summary>
    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            if (_disposed) return;
            DisposeMap();

            lock (_typeRegLock)
            {
                _typeIds.Clear();
                _typeNames = [];
            }
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

    // ----- binary codec (instance methods so they share the per-store type-id table) -----

    // Maps a type string to its small id, registering a new id (interned string) on first sight.
    // Lock-free on the hot path; registration is copy-on-write under _typeRegLock (see field doc).
    private int TypeId(string type)
    {
        if (_typeIds.TryGetValue(type, out int id))
            return id;

        lock (_typeRegLock)
        {
            if (_typeIds.TryGetValue(type, out id))
                return id;

            // Intern so the table holds one shared instance per distinct type name.
            string interned = string.Intern(type);
            var snapshot = _typeNames;
            var grown = new string[snapshot.Length + 1];
            Array.Copy(snapshot, grown, snapshot.Length);
            grown[snapshot.Length] = interned;
            _typeNames = grown; // publish the name BEFORE the id becomes discoverable
            _typeIds[interned] = snapshot.Length;
            return snapshot.Length;
        }
    }

    // Resolves a type id back to its interned string; ids are always valid (written via TypeId).
    private string TypeName(int id)
    {
        var arr = _typeNames;
        return (uint)id < (uint)arr.Length ? arr[id] : "";
    }

    private void EncodeBinary(ParamSet ps, ref BinaryWriterScratch w)
    {
        foreach (var (key, pv) in ps)
        {
            // Key: keys are interned "0".."255"; write as a single byte. A non-byte key (never
            // expected per spec) is clamped to 0 - safe, and the round-trip key comes from the table
            // of param keys on decode, so an out-of-range source key would map to "0".
            w.WriteByte(ParseKeyByte(key));
            w.WriteVarint((uint)TypeId(pv.Type));
            WriteValue(pv.Value, ref w);
        }
    }

    private static byte ParseKeyByte(string key)
    {
        if (byte.TryParse(key, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out byte b))
            return b;
        return 0;
    }

    private static void WriteValue(object? value, ref BinaryWriterScratch w)
    {
        switch (value)
        {
            case null:
                w.WriteByte(KindNull);
                break;
            case long l:
                w.WriteByte(KindLong);
                w.WriteLong(l);
                break;
            // Widen the narrower integer types to Int64 so typed numeric arrays (Byte[], Int16[],
            // Int32[]) round-trip as numbers. Live capture yields byte/short/int elements here; without
            // this they fall to the default branch and get stringified, breaking hex/GUID rendering.
            case byte or sbyte or short or ushort or int or uint:
                w.WriteByte(KindLong);
                w.WriteLong(Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case double d:
                w.WriteByte(KindDouble);
                w.WriteLong(BitConverter.DoubleToInt64Bits(d));
                break;
            case float f:
                w.WriteByte(KindDouble);
                w.WriteLong(BitConverter.DoubleToInt64Bits(f));
                break;
            case bool bo:
                w.WriteByte(KindBool);
                w.WriteByte((byte)(bo ? 1 : 0));
                break;
            case string s:
                w.WriteByte(KindString);
                WriteUtf8(s, ref w);
                break;
            // Dictionary<string, object?> (and any IDictionary) -> tagged dict. Checked before the
            // generic IEnumerable case (a dictionary is also enumerable).
            case System.Collections.IDictionary dict:
                w.WriteByte(KindDict);
                w.WriteVarint((uint)dict.Count);
                foreach (System.Collections.DictionaryEntry kv in dict)
                {
                    WriteUtf8(Convert.ToString(kv.Key, System.Globalization.CultureInfo.InvariantCulture) ?? "", ref w);
                    WriteValue(kv.Value, ref w);
                }
                break;
            // List<object?> and any other sequence (incl. the Byte[] case = a list of longs, and
            // nested lists) -> tagged array. Varint count first so decode can pre-size the list; we
            // do a cheap count pass over the sequence (these are tiny in-memory lists), then write the
            // elements. (Back-patching a varint count after the fact is awkward, so count up front.)
            case System.Collections.IEnumerable seq:
                w.WriteByte(KindArray);
                int n = 0;
                foreach (var _ in seq) n++;
                w.WriteVarint((uint)n);
                foreach (var item in seq)
                    WriteValue(item, ref w);
                break;
            default:
                // Anything unexpected: stringify (mirrors the old JSON codec's TokenToString fallback
                // and ParamCodec.Write's default branch). Decodes back as a String value.
                w.WriteByte(KindString);
                WriteUtf8(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "", ref w);
                break;
        }
    }

    private static void WriteUtf8(string s, ref BinaryWriterScratch w)
    {
        int max = Encoding.UTF8.GetMaxByteCount(s.Length);
        byte[] tmp = ArrayPool<byte>.Shared.Rent(max);
        try
        {
            int n = Encoding.UTF8.GetBytes(s, tmp);
            w.WriteVarint((uint)n);
            w.WriteBytes(tmp.AsSpan(0, n));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(tmp);
        }
    }

    private ParamSet DecodeBinary(ReadOnlySpan<byte> span, int count)
    {
        var entries = new KeyValuePair<string, ParamValue>[count];
        int pos = 0;
        for (int i = 0; i < count; i++)
        {
            byte key = span[pos++];
            int typeId = (int)ReadVarint(span, ref pos);
            object? value = ReadValue(span, ref pos);
            entries[i] = new KeyValuePair<string, ParamValue>(
                ParamKeys.Get(key), new ParamValue(TypeName(typeId), value));
        }
        return new ParamSet(entries);
    }

    private static object? ReadValue(ReadOnlySpan<byte> span, ref int pos)
    {
        byte kind = span[pos++];
        switch (kind)
        {
            case KindNull:
                return null;
            case KindLong:
                return ReadLong(span, ref pos);
            case KindDouble:
                return BitConverter.Int64BitsToDouble(ReadLong(span, ref pos));
            case KindBool:
                return span[pos++] != 0;
            case KindString:
                return ReadUtf8(span, ref pos);
            case KindArray:
            {
                int n = (int)ReadVarint(span, ref pos);
                var list = new List<object?>(n);
                for (int i = 0; i < n; i++)
                    list.Add(ReadValue(span, ref pos));
                return list;
            }
            case KindDict:
            {
                int n = (int)ReadVarint(span, ref pos);
                var dict = new Dictionary<string, object?>(n);
                for (int i = 0; i < n; i++)
                {
                    string k = ReadUtf8(span, ref pos);
                    dict[k] = ReadValue(span, ref pos);
                }
                return dict;
            }
            default:
                return null;
        }
    }

    private static string ReadUtf8(ReadOnlySpan<byte> span, ref int pos)
    {
        int n = (int)ReadVarint(span, ref pos);
        if (n == 0) return "";
        string s = Encoding.UTF8.GetString(span.Slice(pos, n));
        pos += n;
        return s;
    }

    private static long ReadLong(ReadOnlySpan<byte> span, ref int pos)
    {
        long v = BinaryPrimitives.ReadInt64LittleEndian(span.Slice(pos, 8));
        pos += 8;
        return v;
    }

    // LEB128 unsigned varint read.
    private static uint ReadVarint(ReadOnlySpan<byte> span, ref int pos)
    {
        uint result = 0;
        int shift = 0;
        while (true)
        {
            byte b = span[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    /// <summary>
    /// A small, growable, pooled byte writer used only inside <see cref="Append"/> (under the write
    /// lock). A ref struct so it never escapes; backed by an <see cref="ArrayPool{T}"/> buffer that is
    /// returned on <see cref="Dispose"/>. Keeps encode allocation-free across packets.
    /// </summary>
    private ref struct BinaryWriterScratch
    {
        private byte[] _buffer;
        private int _length;

        public BinaryWriterScratch()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(256);
            _length = 0;
        }

        // Adopts an existing pooled buffer at the given write position (BatchEncoder); on grow the
        // old array goes back to the pool and the caller re-reads Buffer/Length after the write.
        public BinaryWriterScratch(byte[] adopt, int length)
        {
            _buffer = adopt;
            _length = length;
        }

        public readonly byte[] Buffer => _buffer;
        public readonly int Length => _length;

        private void Ensure(int extra)
        {
            if (_length + extra <= _buffer.Length) return;
            int newSize = _buffer.Length * 2;
            while (newSize < _length + extra) newSize *= 2;
            byte[] bigger = ArrayPool<byte>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _length).CopyTo(bigger);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = bigger;
        }

        public void WriteByte(byte b)
        {
            Ensure(1);
            _buffer[_length++] = b;
        }

        public void WriteLong(long v)
        {
            Ensure(8);
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_length, 8), v);
            _length += 8;
        }

        public void WriteBytes(ReadOnlySpan<byte> src)
        {
            Ensure(src.Length);
            src.CopyTo(_buffer.AsSpan(_length));
            _length += src.Length;
        }

        // LEB128 unsigned varint write.
        public void WriteVarint(uint value)
        {
            Ensure(5);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_length++] = (byte)value;
        }

        public void Dispose()
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null!;
            }
        }
    }
}
