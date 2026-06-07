using System.Globalization;

namespace AlbionPacketExplorer.PhotonWire;

/// <summary>
/// Convert a decoded GpBinary value (boxed numeric/string/byte[]) to a concrete CLR type. These are
/// plain functional conversions used by consumers (e.g. generated constructors). Independent
/// implementation.
/// </summary>
public static class ObjectExtensions
{
    public static long? ObjectToLong(this object? v) => v switch
    {
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        uint u => u,
        ushort us => us,
        _ => null,
    };

    public static int ObjectToInt(this object? v) => v switch
    {
        int i => i,
        short s => s,
        byte b => b,
        long l when l is >= int.MinValue and <= int.MaxValue => (int) l,
        uint u when u <= int.MaxValue => (int) u,
        ushort us => us,
        _ => 0,
    };

    public static short ObjectToShort(this object? v) => v switch
    {
        short s => s,
        byte b => b,
        int i when i is >= short.MinValue and <= short.MaxValue => (short) i,
        long l when l is >= short.MinValue and <= short.MaxValue => (short) l,
        _ => 0,
    };

    public static byte ObjectToByte(this object? v) => v switch
    {
        byte b => b,
        short s when s is >= 0 and <= 255 => (byte) s,
        int i when i is >= 0 and <= 255 => (byte) i,
        long l when l is >= 0 and <= 255 => (byte) l,
        _ => 0,
    };

    public static bool ObjectToBool(this object? v) => v switch
    {
        bool b => b,
        byte by => by != 0,
        int i => i != 0,
        _ => false,
    };

    public static double ObjectToDouble(this object? v) => v switch
    {
        double d => d,
        float f => f,
        long l => l,
        int i => i,
        short s => s,
        byte b => b,
        string str when double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) => r,
        _ => 0d,
    };

    public static Guid? ObjectToGuid(this object? v)
    {
        try
        {
            if (v is byte[] { Length: 16 } bytes) return new Guid(bytes);
            if (v is System.Collections.IEnumerable e)
            {
                var arr = e.OfType<byte>().ToArray();
                if (arr.Length == 16) return new Guid(arr);
            }
        }
        catch { /* not a guid */ }
        return null;
    }
}
