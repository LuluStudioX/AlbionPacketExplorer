using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [123] NewMob
public sealed class NewMobEvent
{
    public long ObjectId { get; }
    public int MobIndex { get; }
    public double MoveSpeed { get; }
    public double HitPoints { get; }
    public double HitPointsMax { get; }
    public double Energy { get; }
    public double EnergyMax { get; }
    public double EnergyRegeneration { get; }

    public NewMobEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) MobIndex = v1.ObjectToInt();
        if (p.TryGetValue(11, out var v11)) MoveSpeed = v11.ObjectToDouble();
        if (p.TryGetValue(13, out var v13)) HitPoints = v13.ObjectToDouble();
        if (p.TryGetValue(14, out var v14)) HitPointsMax = v14.ObjectToDouble();
        if (p.TryGetValue(17, out var v17)) Energy = v17.ObjectToDouble();
        if (p.TryGetValue(18, out var v18)) EnergyMax = v18.ObjectToDouble();
        if (p.TryGetValue(19, out var v19)) EnergyRegeneration = v19.ObjectToDouble();
    }
}
