using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [431] ReceivedGvgSeasonPoints
public sealed class ReceivedGvgSeasonPointsEvent
{
    public int SeasonPoints { get; }
    public ReceivedGvgSeasonPointsEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) SeasonPoints = v1.ObjectToInt();
    }
}
