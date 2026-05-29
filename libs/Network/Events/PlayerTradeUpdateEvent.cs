using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [179] PlayerTradeUpdate
public sealed class PlayerTradeUpdateEvent
{
    public long TradeId { get; }
    public long Revision { get; }
    public long LocalSilver { get; }
    public long PartnerSilver { get; }

    public PlayerTradeUpdateEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) TradeId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) Revision = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) LocalSilver = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) PartnerSilver = v4.ObjectToLong() ?? 0;
    }
}
