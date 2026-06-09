using System.Globalization;
using AlbionPacketExplorer.Models;
using AlbionPacketExplorer.Network;
using AlbionPacketExplorer.PhotonWire;

namespace AlbionPacketExplorer.Services;

/// <summary>
/// Decodes raw Albion UDP payloads into <see cref="PacketEntry"/> via the independent PhotonWire
/// reader. The real wire code lives in the parameter echo (key 252 on events, 253 on requests and
/// responses); the message's own code byte is only a fallback.
/// </summary>
public sealed class RawAlbionParser : IPacketReceiver
{
    public event Action<PacketEntry>? PacketReceived;

    /// <summary>Raised with the raw packet payload (before decode) so a session can be saved as RAW.</summary>
    public event Action<byte[]>? RawReceived;

    private readonly PhotonPacketReader _reader = new();

    public RawAlbionParser()
    {
        _reader.OnEvent += e => Emit("EVENT", WireCode(e.Parameters, 252, e.Code), e.Parameters);
        _reader.OnRequest += e => Emit("REQUEST", WireCode(e.Parameters, 253, e.OperationCode), e.Parameters);
        _reader.OnResponse += e =>
            Emit("RESPONSE", WireCode(e.Parameters, 253, e.OperationCode), e.Parameters, e.ReturnCode, e.DebugMessage);
    }

    public void ReceivePacket(byte[] payload)
    {
        RawPacketLog.MaybeSave(payload);
        RawReceived?.Invoke(payload);
        try { _reader.ReadPacket(payload); }
        catch { /* malformed / partial packet: skip, keep the capture alive */ }
    }

    private void Emit(string kind, short code, IReadOnlyDictionary<byte, object?> parameters,
        short? returnCode = null, string? debugMessage = null)
    {
        if (code < 0) return;

        // Photon parameter keys are distinct bytes, so pack straight into the array (no dedupe needed).
        var entries = new KeyValuePair<string, ParamValue>[parameters.Count];
        int i = 0;
        foreach (var (k, v) in parameters)
            // Reuse the shared "0".."255" key and intern the type name so both dedupe across packets.
            entries[i++] = new KeyValuePair<string, ParamValue>(ParamKeys.Get(k), new ParamValue(string.Intern(GetTypeName(v)), v));

        PacketReceived?.Invoke(new PacketEntry(DateTime.UtcNow, string.Intern(kind), code, new ParamSet(entries),
            returnCode, string.IsNullOrEmpty(debugMessage) ? null : debugMessage));
    }

    // Prefer the wire-code echo (key 252/253); fall back to the message's own code byte.
    private static short WireCode(IReadOnlyDictionary<byte, object?> parameters, byte echoKey, byte fallback)
    {
        if (parameters.TryGetValue(echoKey, out var v))
        {
            try { return checked((short) Convert.ToInt32(v, CultureInfo.InvariantCulture)); }
            catch { /* fall through to the byte code */ }
        }
        return fallback;
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
        long[] => "Int64[]",
        float[] => "Single[]",
        double[] => "Double[]",
        bool[] => "Boolean[]",
        string[] => "String[]",
        _ => v.GetType().Name
    };
}
