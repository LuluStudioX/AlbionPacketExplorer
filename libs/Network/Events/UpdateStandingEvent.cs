using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [87] UpdateStanding
public sealed class UpdateStandingEvent
{
    public long TotalPoints { get; }
    public long PremiumBonus { get; }

    public UpdateStandingEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) TotalPoints = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) PremiumBonus = v1.ObjectToLong() ?? 0;
    }
}
