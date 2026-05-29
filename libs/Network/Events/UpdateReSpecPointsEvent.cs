using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [84] UpdateReSpecPoints
public sealed class UpdateReSpecPointsEvent
{
    public long GainedReSpecPoints { get; }
    public long PaidSilver { get; }

    public UpdateReSpecPointsEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(2, out var v2)) GainedReSpecPoints = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) PaidSilver = v3.ObjectToLong() ?? 0;
    }
}
