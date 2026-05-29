using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.PhotonPackageParser;

namespace AlbionPacketExplorer.Services;

public sealed class RawAlbionParser : PhotonParser
{
    public event Action<PacketEntry>? PacketReceived;

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
    {
        short eventCode = ReadPhotonCode(parameters, 252);
        if (eventCode < 0) return;
        PacketReceived?.Invoke(BuildEntry("EVENT", eventCode, parameters));
    }

    protected override void OnRequest(byte operationCode, Dictionary<byte, object> parameters)
    {
        short opCode = ReadPhotonCode(parameters, 253);
        if (opCode < 0) opCode = operationCode;
        PacketReceived?.Invoke(BuildEntry("REQUEST", opCode, parameters));
    }

    protected override void OnResponse(byte operationCode, short returnCode, string debugMessage, Dictionary<byte, object> parameters)
    {
        short opCode = ReadPhotonCode(parameters, 253);
        if (opCode < 0) opCode = operationCode;
        PacketReceived?.Invoke(BuildEntry("RESPONSE", opCode, parameters));
    }

    private static PacketEntry BuildEntry(string kind, short code, Dictionary<byte, object> parameters)
    {
        var @params = new Dictionary<string, ParamValue>(parameters.Count);
        foreach (var (k, v) in parameters)
            @params[k.ToString()] = new ParamValue(GetTypeName(v), v);

        return new PacketEntry(DateTime.UtcNow, kind, code, @params);
    }

    private static short ReadPhotonCode(Dictionary<byte, object> parameters, byte key)
    {
        if (!parameters.TryGetValue(key, out var v)) return -1;
        try { return checked((short)Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture)); }
        catch (Exception) { return -1; }
    }

    private static string GetTypeName(object? v) => v switch
    {
        null => "Null",
        long => "Int64",
        int => "Int32",
        short => "Int16",
        byte => "Byte",
        bool => "Boolean",
        float => "Single",
        double => "Double",
        string => "String",
        byte[] => "Byte[]",
        short[] => "Int16[]",
        int[] => "Int32[]",
        float[] => "Single[]",
        _ => v.GetType().Name
    };
}
