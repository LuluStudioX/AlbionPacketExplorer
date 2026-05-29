using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [85] UpdateCurrency
public sealed class UpdateCurrencyEvent
{
    public byte CityFaction { get; }
    public long GainedFactionCoins { get; }
    public long FactionRankPoints { get; }
    public long TotalPlayerFactionPoints { get; }

    public UpdateCurrencyEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(2, out var v2)) CityFaction = v2.ObjectToByte();
        if (p.TryGetValue(3, out var v3)) GainedFactionCoins = v3.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) FactionRankPoints = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(9, out var v9)) TotalPlayerFactionPoints = v9.ObjectToLong() ?? 0;
    }
}
