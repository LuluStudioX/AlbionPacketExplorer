using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// REQUEST [45] ActionOnBuildingStart
// 0=ticks 1=buildingObjectId 2=actionType 4=costs 7=itemIndex 9=quantity 253=45
public sealed class ActionOnBuildingStartRequest
{
    public long Ticks { get; }
    public long BuildingObjectId { get; }
    public int ActionType { get; }
    public long Costs { get; }
    public int ItemIndex { get; }
    public int Quantity { get; }

    public ActionOnBuildingStartRequest(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) Ticks = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) BuildingObjectId = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) ActionType = v2.ObjectToInt();
        if (p.TryGetValue(4, out var v4)) Costs = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(7, out var v7)) ItemIndex = v7.ObjectToInt();
        if (p.TryGetValue(9, out var v9)) Quantity = v9.ObjectToInt();
    }
}
