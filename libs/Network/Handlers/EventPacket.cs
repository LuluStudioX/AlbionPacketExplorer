namespace AlbionPacketExplorer.Network.Handlers;

public sealed class EventPacket(short eventCode, Dictionary<byte, object> parameters)
{
    public short EventCode { get; } = eventCode;
    public Dictionary<byte, object> Parameters { get; } = parameters;
}

public sealed class RequestPacket(short operationCode, Dictionary<byte, object> parameters)
{
    public short OperationCode { get; } = operationCode;
    public Dictionary<byte, object> Parameters { get; } = parameters;
}

public sealed class ResponsePacket(short operationCode, Dictionary<byte, object> parameters)
{
    public short OperationCode { get; } = operationCode;
    public Dictionary<byte, object> Parameters { get; } = parameters;
}
