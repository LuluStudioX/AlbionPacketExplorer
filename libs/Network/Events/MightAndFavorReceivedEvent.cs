using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [494] MightAndFavorReceived
public sealed class MightAndFavorReceivedEvent
{
    public long Might { get; }
    public long BonusOfMight { get; }
    public long PremiumOfMight { get; }
    public long Favor { get; }
    public long BonusOfFavor { get; }
    public long PremiumOfFavor { get; }
    public long TotalFavor { get; }
    public MightAndFavorReceivedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) Might = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) BonusOfMight = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) PremiumOfMight = v3.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) Favor = v4.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5)) BonusOfFavor = v5.ObjectToLong() ?? 0;
        if (p.TryGetValue(6, out var v6)) PremiumOfFavor = v6.ObjectToLong() ?? 0;
        if (p.TryGetValue(7, out var v7)) TotalFavor = v7.ObjectToLong() ?? 0;
    }
}
