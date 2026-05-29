using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [57] FarmableObjectInfo
// 0=objectId 1=isReady 2=itemId 3=growTime 5=plantedAt 252=57
public sealed class FarmableObjectInfoEvent
{
    public long ObjectId { get; }
    public bool IsReady { get; }
    public int ItemId { get; }
    public long GrowTime { get; }
    public DateTime? PlantedAt { get; }

    public FarmableObjectInfoEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) IsReady = v1.ObjectToBool();
        if (p.TryGetValue(2, out var v2)) ItemId = v2.ObjectToInt();
        if (p.TryGetValue(3, out var v3)) GrowTime = v3.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5))
        {
            var ticks = v5.ObjectToLong();
            if (ticks is > 0) PlantedAt = new DateTime(ticks.Value, DateTimeKind.Utc);
        }
    }
}
