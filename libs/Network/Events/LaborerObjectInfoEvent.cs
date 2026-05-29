using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [30] LaborerObjectInfo
// 0=objectId 1=itemId 2=tier 4=returnTime 5=owner 6=lootState 7=capacity
// 8=itemIds[] 9=quantities[] 10=isAwayOnJob 252=30
public sealed class LaborerObjectInfoEvent
{
    public long ObjectId { get; }
    public int ItemId { get; }
    public int Tier { get; }
    public long ReturnTime { get; }
    public string Owner { get; }
    public int LootState { get; }
    public int Capacity { get; }
    public object? ItemIds { get; }
    public object? Quantities { get; }
    public bool IsAwayOnJob { get; }

    public LaborerObjectInfoEvent(Dictionary<byte, object> p)
    {
        Owner = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) ItemId = v1.ObjectToInt();
        if (p.TryGetValue(2, out var v2)) Tier = v2.ObjectToInt();
        if (p.TryGetValue(4, out var v4)) ReturnTime = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5)) Owner = v5?.ToString() ?? string.Empty;
        if (p.TryGetValue(6, out var v6)) LootState = v6.ObjectToInt();
        if (p.TryGetValue(7, out var v7)) Capacity = v7.ObjectToInt();
        if (p.TryGetValue(8, out var v8)) ItemIds = v8;
        if (p.TryGetValue(9, out var v9)) Quantities = v9;
        if (p.TryGetValue(10, out var v10)) IsAwayOnJob = v10.ObjectToBool();
    }
}
