using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [6] HealthUpdate
public sealed class HealthUpdateEvent
{
    public long AffectedObjectId { get; }
    public long Timestamp { get; }
    public double HealthChange { get; }
    public double NewHealthValue { get; }
    public byte EffectType { get; }
    public byte EffectOrigin { get; }
    public long CauserId { get; }
    public short CausingSpellIndex { get; }

    public HealthUpdateEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) AffectedObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) Timestamp = v1.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) HealthChange = v2.ObjectToDouble();
        if (p.TryGetValue(3, out var v3)) NewHealthValue = v3.ObjectToDouble();
        if (p.TryGetValue(4, out var v4)) EffectType = v4.ObjectToByte();
        if (p.TryGetValue(5, out var v5)) EffectOrigin = v5.ObjectToByte();
        if (p.TryGetValue(6, out var v6)) CauserId = v6.ObjectToLong() ?? 0;
        if (p.TryGetValue(7, out var v7)) CausingSpellIndex = v7.ObjectToShort();
    }
}
