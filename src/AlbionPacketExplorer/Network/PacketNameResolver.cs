using System.Collections.Concurrent;

namespace AlbionPacketExplorer.Network;

public static class PacketNameResolver
{
    // Only a few hundred distinct (kind, code) pairs exist, but Resolve is called millions of
    // times per filter pass. Memoize so repeated calls are O(1) instead of re-running
    // ToUpperInvariant + Enum.IsDefined + enum ToString each time. The value factory is pure,
    // so ConcurrentDictionary's thread-safety is sufficient.
    private static readonly ConcurrentDictionary<(string Kind, int Code), string> Cache = new();

    public static string Resolve(string kind, int code) =>
        Cache.GetOrAdd((kind, code), static key => key.Kind.ToUpperInvariant() switch
        {
            "EVENT" => Enum.IsDefined(typeof(EventCodes), key.Code)
                ? ((EventCodes)key.Code).ToString()
                : string.Empty,
            "REQUEST" or "RESPONSE" => Enum.IsDefined(typeof(OperationCodes), key.Code)
                ? ((OperationCodes)key.Code).ToString()
                : string.Empty,
            _ => string.Empty
        });
}
