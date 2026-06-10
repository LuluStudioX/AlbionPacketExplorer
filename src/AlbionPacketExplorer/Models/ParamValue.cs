namespace AlbionPacketExplorer.Models;

// A value type (not a heap class) so a packet's params carry no per-value 16-byte object header.
//
// Numerics are stored UNBOXED in the inline fields below: at ~4M packets x ~4 params, keeping
// long/double/bool boxed on the heap meant ~16M retained boxed objects (one object header each,
// plus GC churn and fragmentation). The tagged union packs every numeric into _num (8 bytes) and
// only falls back to a heap reference (_ref) for genuinely reference-typed payloads (strings,
// List<object?>, Dictionary<...>). The public surface (Type, Value, and value-equality) is
// identical to the previous `record struct ParamValue(string Type, object? Value)`, so all callers
// compile and behave unchanged; Value reconstructs the same boxed numeric on demand (a transient
// gen0 box, which is acceptable - only the RETAINED box mattered).
public readonly record struct ParamValue
{
    private const byte TagNull = 0;
    private const byte TagInt64 = 1;
    private const byte TagDouble = 2;
    private const byte TagBool = 3;
    private const byte TagRef = 4;

    public string Type { get; }

    private readonly byte _tag;     // discriminator: Null / Int64 / Double / Bool / Ref
    private readonly long _num;     // Int64 value, OR BitConverter double bits, OR bool (0/1)
    private readonly object? _ref;  // strings, List<object?>, Dictionary<...>, anything non-numeric

    public ParamValue(string type, object? value)
    {
        Type = type;

        switch (value)
        {
            case null:
                _tag = TagNull;
                break;

            // Native numeric tags the parsers produce directly.
            case long l:
                _tag = TagInt64;
                _num = l;
                break;
            case double d:
                _tag = TagDouble;
                _num = BitConverter.DoubleToInt64Bits(d);
                break;
            case bool b:
                _tag = TagBool;
                _num = b ? 1 : 0;
                break;

            // Narrower integers the live decoder may emit: widen into the Int64 tag (lossless).
            case int i:
                _tag = TagInt64;
                _num = i;
                break;
            case short s:
                _tag = TagInt64;
                _num = s;
                break;
            case byte by:
                _tag = TagInt64;
                _num = by;
                break;
            case sbyte sb:
                _tag = TagInt64;
                _num = sb;
                break;
            case ushort us:
                _tag = TagInt64;
                _num = us;
                break;
            case uint ui:
                _tag = TagInt64;
                _num = ui;
                break;

            // float widens into the Double tag (lossless float->double).
            case float f:
                _tag = TagDouble;
                _num = BitConverter.DoubleToInt64Bits(f);
                break;

            // Everything else (string, List<object?>, Dictionary<string,object?>, arrays, ulong,
            // decimal, or any unanticipated type) is kept as-is so nothing is ever lost.
            default:
                _tag = TagRef;
                _ref = value;
                break;
        }
    }

    /// <summary>
    /// The original payload. Numerics box on demand here (a transient gen0 allocation), so readers
    /// that pattern-match on <c>long</c>/<c>double</c> still match. Note: widened integers come back
    /// as <c>long</c> and <c>float</c> comes back as <c>double</c>.
    /// </summary>
    public object? Value => _tag switch
    {
        TagInt64 => _num,
        TagDouble => BitConverter.Int64BitsToDouble(_num),
        TagBool => _num != 0,
        TagRef => _ref,
        _ => null,
    };

    /// <summary>
    /// Builds the value-distribution key straight from the tagged union, with NO boxing - the stats
    /// load hot path calls this ~16M times per big file, and going through <see cref="Value"/> would
    /// box every numeric into a transient gen0 object just to read it back. Numerics/bools carry
    /// their bits inline; only strings and array/dict reprs allocate (the repr is needed to dedupe
    /// equal contents, and those types are the minority).
    /// </summary>
    public StatKey ToStatKey() => _tag switch
    {
        TagInt64 => StatKey.FromLong(_num),
        TagDouble => StatKey.FromDouble(BitConverter.Int64BitsToDouble(_num)),
        TagBool => StatKey.FromBool(_num != 0),
        TagRef => _ref is string s ? StatKey.FromText(s)
                                   : StatKey.FromText(Services.PacketDisplayFormatter.FormatParamValue(this)),
        _ => StatKey.Null,
    };
}
