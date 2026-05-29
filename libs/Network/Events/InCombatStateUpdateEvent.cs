using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [276] InCombatStateUpdate
public sealed class InCombatStateUpdateEvent
{
    public long ObjectId { get; }
    public bool InActiveCombat { get; }
    public bool InPassiveCombat { get; }
    public InCombatStateUpdateEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) InActiveCombat = v1.ObjectToBool();
        if (p.TryGetValue(2, out var v2)) InPassiveCombat = v2.ObjectToBool();
    }
}
