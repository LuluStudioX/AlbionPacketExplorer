using AlbionPacketExplorer.PhotonPackageParser;
using System.Globalization;

namespace AlbionPacketExplorer.Network.Handlers;

public sealed class AlbionNetworkParser : PhotonParser
{
    private readonly HandlersCollection _handlers = new();

    public void AddHandler<TPacket>(PacketHandler<TPacket> handler)
        => _handlers.Add(handler);

    public void AddEventHandler<TEvent>(EventPacketHandler<TEvent> handler)
        => AddHandler(handler);

    public void AddRequestHandler<TOperation>(RequestPacketHandler<TOperation> handler)
        => AddHandler(handler);

    public void AddResponseHandler<TOperation>(ResponsePacketHandler<TOperation> handler)
        => AddHandler(handler);

    protected override void OnEvent(byte code, Dictionary<byte, object> parameters)
    {
        var eventCode = ParseCode(parameters, 252);
        if (eventCode < 0) return;
        _ = _handlers.HandleAsync(new EventPacket(eventCode, parameters));
    }

    protected override void OnRequest(byte operationCodeByte, Dictionary<byte, object> parameters)
    {
        var opCode = ParseCode(parameters, 253);
        if (opCode < 0) return;
        _ = _handlers.HandleAsync(new RequestPacket(opCode, parameters));
    }

    protected override void OnResponse(byte operationCodeByte, short returnCode, string debugMessage, Dictionary<byte, object> parameters)
    {
        var opCode = ParseCode(parameters, 253);
        if (opCode < 0) return;
        _ = _handlers.HandleAsync(new ResponsePacket(opCode, parameters));
    }

    private static short ParseCode(Dictionary<byte, object> parameters, byte key)
    {
        if (!parameters.TryGetValue(key, out var value)) return -1;
        try { return checked((short)Convert.ToInt32(value, CultureInfo.InvariantCulture)); }
        catch { return -1; }
    }
}
