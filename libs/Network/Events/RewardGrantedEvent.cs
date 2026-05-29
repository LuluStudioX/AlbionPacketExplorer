using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [265] RewardGranted
public sealed class RewardGrantedEvent
{
    public int ItemIndex { get; }
    public int Quantity { get; }
    public RewardGrantedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) ItemIndex = v1.ObjectToInt();
        if (p.TryGetValue(3, out var v3)) Quantity = v3.ObjectToInt();
    }
}
