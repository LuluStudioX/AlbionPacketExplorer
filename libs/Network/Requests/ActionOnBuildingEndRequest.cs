using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// REQUEST [46] ActionOnBuildingEnd
// 0=buildingObjectId 253=46
public sealed class ActionOnBuildingEndRequest
{
    public long BuildingObjectId { get; }

    public ActionOnBuildingEndRequest(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) BuildingObjectId = v0.ObjectToLong() ?? 0;
    }
}
