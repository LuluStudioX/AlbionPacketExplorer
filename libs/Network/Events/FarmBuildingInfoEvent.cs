using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [54] FarmBuildingInfo
// 0=objectId 4=elapsedGrowTime(100µs) 5=serverNow(ticks)
public sealed class FarmBuildingInfoEvent
{
    public long ObjectId { get; }
    public long ElapsedGrowTime100us { get; }
    public DateTime? ServerNow { get; }

    public FarmBuildingInfoEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) ElapsedGrowTime100us = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5))
        {
            var ticks = v5.ObjectToLong();
            if (ticks is > 0) ServerNow = new DateTime(ticks.Value, DateTimeKind.Utc);
        }
    }
}
