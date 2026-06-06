namespace AlbionPacketExplorer.Network.Handlers;

public static class PacketObjectExtensions
{
    public static long? ObjectToLong(this object value) => value switch
    {
        byte v   => v,
        short v  => v,
        ushort v => v,
        int v    => v,
        uint v   => v,
        long v   => v,
        _        => null
    };

    public static int ObjectToInt(this object value) => value switch
    {
        byte v                                           => v,
        short v                                          => v,
        ushort v                                         => v,
        int v                                            => v,
        uint v when v <= int.MaxValue                    => (int)v,
        long v when v is >= int.MinValue and <= int.MaxValue => (int)v,
        _                                                => 0
    };

    public static short ObjectToShort(this object value) => value switch
    {
        byte v                                                 => v,
        short v                                                => v,
        ushort v when v <= short.MaxValue                      => (short)v,
        int v when v is >= short.MinValue and <= short.MaxValue => (short)v,
        long v when v is >= short.MinValue and <= short.MaxValue => (short)v,
        _                                                      => 0
    };

    public static byte ObjectToByte(this object value) => value switch
    {
        byte v                                               => v,
        short v when v is >= byte.MinValue and <= byte.MaxValue => (byte)v,
        int v when v is >= byte.MinValue and <= byte.MaxValue   => (byte)v,
        long v when v is >= byte.MinValue and <= byte.MaxValue  => (byte)v,
        _                                                    => 0
    };

    public static bool ObjectToBool(this object value) => value as bool? ?? false;

    public static double ObjectToDouble(this object value) => value switch
    {
        byte v   => v,
        short v  => v,
        ushort v => v,
        int v    => v,
        uint v   => v,
        long v   => v,
        float v  => v,
        double v => v,
        _        => 0d
    };

    public static Guid? ObjectToGuid(this object value)
    {
        try
        {
            if (value is byte[] bytes && bytes.Length == 16)
                return new Guid(bytes);
            if (value is System.Collections.IEnumerable enumerable)
            {
                var arr = enumerable.OfType<byte>().ToArray();
                if (arr.Length == 16) return new Guid(arr);
            }
        }
        catch { }
        return null;
    }
}
