using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [231] PartyJoined
public sealed class PartyJoinedEvent
{
    public string[] UserNames { get; }
    public PartyJoinedEvent(Dictionary<byte, object> p)
    {
        UserNames = [];
        if (p.TryGetValue(6, out var v6) && v6 is object[] arr)
            UserNames = arr.Select(x => x?.ToString() ?? string.Empty).ToArray();
        else if (p.TryGetValue(6, out var v6b) && v6b is string[] sarr)
            UserNames = sarr;
    }
}

// EVENT [232] PartyDisbanded
public sealed class PartyDisbandedEvent
{
    public PartyDisbandedEvent(Dictionary<byte, object> _) { }
}

// EVENT [233] PartyPlayerJoined
public sealed class PartyPlayerJoinedEvent
{
    public Guid UserGuid { get; }
    public string Username { get; }
    public PartyPlayerJoinedEvent(Dictionary<byte, object> p)
    {
        Username = string.Empty;
        if (p.TryGetValue(1, out var v1)) UserGuid = v1.ObjectToGuid() ?? Guid.Empty;
        if (p.TryGetValue(2, out var v2)) Username = v2?.ToString() ?? string.Empty;
    }
}

// EVENT [234] PartyChangedOrder
public sealed class PartyChangedOrderEvent
{
    public PartyChangedOrderEvent(Dictionary<byte, object> _) { }
}

// EVENT [235] PartyPlayerLeft
public sealed class PartyPlayerLeftEvent
{
    public Guid UserGuid { get; }
    public PartyPlayerLeftEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) UserGuid = v1.ObjectToGuid() ?? Guid.Empty;
    }
}

// EVENT [238] PartySilverGained
public sealed class PartySilverGainedEvent
{
    public long Timestamp { get; }
    public long TargetEntityId { get; }
    public long SilverNet { get; }
    public long SilverPreTax { get; }
    public PartySilverGainedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) Timestamp = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) TargetEntityId = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) SilverNet = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) SilverPreTax = v3.ObjectToLong() ?? 0;
    }
}
