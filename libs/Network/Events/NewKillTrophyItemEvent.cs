using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [34] NewKillTrophyItem
public sealed class NewKillTrophyItemEvent
{
    public long ObjectId { get; }
    public int ItemId { get; }
    public int Quantity { get; }
    public long EstimatedMarketValue { get; }
    public long Durability { get; }

    public NewKillTrophyItemEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) ItemId = v1.ObjectToInt();
        if (p.TryGetValue(2, out var v2)) Quantity = v2.ObjectToInt();
        if (p.TryGetValue(4, out var v4)) EstimatedMarketValue = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(7, out var v7)) Durability = v7.ObjectToLong() ?? 0;
    }
}
