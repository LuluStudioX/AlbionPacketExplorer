using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [62] TakeSilver
public sealed class TakeSilverEvent
{
    public long ObjectId { get; }
    public long Timestamp { get; }
    public long TargetEntityId { get; }
    public long YieldPreTax { get; }
    public long GuildTax { get; }
    public long ClusterTax { get; }
    public bool IsPremiumBonus { get; }
    public long Multiplier { get; }

    public TakeSilverEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) Timestamp = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) TargetEntityId = v2.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) YieldPreTax = v3.ObjectToLong() ?? 0;
        if (p.TryGetValue(5, out var v5)) GuildTax = v5.ObjectToLong() ?? 0;
        if (p.TryGetValue(6, out var v6)) ClusterTax = v6.ObjectToLong() ?? 0;
        if (p.TryGetValue(7, out var v7)) IsPremiumBonus = v7.ObjectToBool();
        if (p.TryGetValue(8, out var v8)) Multiplier = v8.ObjectToLong() ?? 0;
    }
}
