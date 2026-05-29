using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [178] PlayerTradeCancel / [180] PlayerTradeFinished — same shape
public sealed class PlayerTradeCancelEvent
{
    public long TradeId { get; }
    public PlayerTradeCancelEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) TradeId = v0.ObjectToLong() ?? 0;
    }
}

public sealed class PlayerTradeFinishedEvent
{
    public long TradeId { get; }
    public PlayerTradeFinishedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) TradeId = v0.ObjectToLong() ?? 0;
    }
}
