using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [66] ActionOnBuildingFinished
public sealed class ActionOnBuildingFinishedEvent
{
    public long UserObjectId { get; }
    public long BuildingObjectId { get; }
    public long ActionType { get; }

    public ActionOnBuildingFinishedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) UserObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) BuildingObjectId = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) ActionType = v4.ObjectToLong() ?? 0;
    }
}
