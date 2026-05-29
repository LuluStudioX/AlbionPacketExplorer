using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [32] NewSimpleItem
// 0=objectId 1=itemIndex 2=quantity 4=estimatedMarketValue 7=durability 252=32
public sealed class NewSimpleItemEvent
{
    public long ObjectId { get; }
    public int ItemIndex { get; }
    public int Quantity { get; }
    public long EstimatedMarketValue { get; }
    public long Durability { get; }

    public NewSimpleItemEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) ItemIndex = v1.ObjectToInt();
        if (p.TryGetValue(2, out var v2)) Quantity = v2.ObjectToInt();
        if (p.TryGetValue(4, out var v4)) EstimatedMarketValue = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(7, out var v7)) Durability = v7.ObjectToLong() ?? 0;
    }
}
