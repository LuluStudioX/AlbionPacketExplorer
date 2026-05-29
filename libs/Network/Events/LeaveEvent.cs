using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [1] Leave
public sealed class LeaveEvent
{
    public long ObjectId { get; }
    public LeaveEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
    }
}
