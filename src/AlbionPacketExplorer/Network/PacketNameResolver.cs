namespace AlbionPacketExplorer.Network;

public static class PacketNameResolver
{
    public static string Resolve(string kind, int code) => kind.ToUpperInvariant() switch
    {
        "EVENT" => Enum.IsDefined(typeof(EventCodes), code)
            ? ((EventCodes)code).ToString()
            : string.Empty,
        "REQUEST" or "RESPONSE" => Enum.IsDefined(typeof(OperationCodes), code)
            ? ((OperationCodes)code).ToString()
            : string.Empty,
        _ => string.Empty
    };
}
