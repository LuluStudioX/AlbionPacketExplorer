using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [72] UpdateFame
// 1=totalFame 2=fameWithMultiplier 3=zoneFame 4=multiplier 5=isPremiumBonus
// 8=bagInsightItemIndex 10=satchelFame 17=bonusFactor 252=72
public sealed class UpdateFameEvent
{
    public long TotalFame { get; }
    public long FameWithMultiplier { get; }
    public long ZoneFame { get; }
    public long Multiplier { get; }
    public bool IsPremiumBonus { get; }
    public int BagInsightItemIndex { get; }
    public long SatchelFame { get; }
    public long BonusFactor { get; }

    public UpdateFameEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) TotalFame = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) FameWithMultiplier = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) ZoneFame = v3.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) Multiplier = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5)) IsPremiumBonus = v5.ObjectToBool();
        if (p.TryGetValue(8, out var v8)) BagInsightItemIndex = v8.ObjectToInt();
        if (p.TryGetValue(10, out var v10)) SatchelFame = v10.ObjectToLong() ?? 0;
        if (p.TryGetValue(17, out var v17)) BonusFactor = v17.ObjectToLong() ?? 0;
    }
}
