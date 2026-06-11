using System.Collections.Concurrent;

namespace AlbionPacketExplorer.Network;

public static class PacketNameResolver
{
    // Only a few hundred distinct (kind, code) pairs exist, but Resolve is called millions of
    // times per filter pass. Memoize so repeated calls are O(1) instead of re-running
    // ToUpperInvariant + Enum.IsDefined + enum ToString each time. The value factory is pure,
    // so ConcurrentDictionary's thread-safety is sufficient.
    private static readonly ConcurrentDictionary<(string Kind, int Code), string> Cache = new();

    // Runtime overrides (domain "EVENT"/"OP" -> code -> name) for codes the compiled enums don't
    // know yet, or name wrong after a patch. Supplied by ProtocolOverrideStore; takes precedence so
    // a shifted code shows the live client's name. Replacing the set clears the memoization.
    private static volatile IReadOnlyDictionary<(string Domain, int Code), string> _overrides =
        new Dictionary<(string, int), string>();

    public static void SetOverrides(IReadOnlyDictionary<(string Domain, int Code), string> overrides)
    {
        _overrides = overrides;
        Cache.Clear();
    }

    public static string Resolve(string kind, int code) =>
        Cache.GetOrAdd((kind, code), static key =>
        {
            switch (key.Kind.ToUpperInvariant())
            {
                case "EVENT":
                    if (_overrides.TryGetValue(("EVENT", key.Code), out var ev) && ev.Length > 0) return ev;
                    return Enum.IsDefined(typeof(EventCodes), key.Code)
                        ? ((EventCodes)key.Code).ToString() : string.Empty;
                case "REQUEST":
                case "RESPONSE":
                    if (_overrides.TryGetValue(("OP", key.Code), out var op) && op.Length > 0) return op;
                    return Enum.IsDefined(typeof(OperationCodes), key.Code)
                        ? ((OperationCodes)key.Code).ToString() : string.Empty;
                default:
                    return string.Empty;
            }
        });
}
