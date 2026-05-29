using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [86] UpdateFactionStanding
public sealed class UpdateFactionStandingEvent
{
    public byte CityFaction { get; }
    public long GainedFactionFlagPoints { get; }
    public long BonusPremiumGained { get; }
    public long TotalPlayerFactionFlagPoints { get; }

    public UpdateFactionStandingEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) CityFaction = v0.ObjectToByte();
        if (p.TryGetValue(1, out var v1)) GainedFactionFlagPoints = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) BonusPremiumGained = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) TotalPlayerFactionFlagPoints = v3.ObjectToLong() ?? 0;
    }
}
