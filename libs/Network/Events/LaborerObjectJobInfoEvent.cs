using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [35] LaborerObjectJobInfo
// 0=objectId 1=journalItemId 2=isLootReady 4=returnTime 5=owner 6=capacity 252=35
public sealed class LaborerObjectJobInfoEvent
{
    public long ObjectId { get; }
    public int JournalItemId { get; }
    public bool IsLootReady { get; }
    public long ReturnTime { get; }
    public string Owner { get; }
    public int Capacity { get; }

    public LaborerObjectJobInfoEvent(Dictionary<byte, object> p)
    {
        Owner = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) JournalItemId = v1.ObjectToInt();
        if (p.TryGetValue(2, out var v2)) IsLootReady = v2.ObjectToBool();
        if (p.TryGetValue(4, out var v4)) ReturnTime = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5)) Owner = v5?.ToString() ?? string.Empty;
        if (p.TryGetValue(6, out var v6)) Capacity = v6.ObjectToInt();
    }
}
