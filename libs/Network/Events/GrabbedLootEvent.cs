using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [277] OtherGrabbedLoot
public sealed class GrabbedLootEvent
{
    public string LootedFromName { get; }
    public string LooterName { get; }
    public bool IsSilver { get; }
    public int ItemIndex { get; }
    public int Quantity { get; }
    public GrabbedLootEvent(Dictionary<byte, object> p)
    {
        LootedFromName = LooterName = string.Empty;
        if (p.TryGetValue(1, out var v1)) LootedFromName = v1?.ToString() ?? string.Empty;
        if (p.TryGetValue(2, out var v2)) LooterName = v2?.ToString() ?? string.Empty;
        if (p.TryGetValue(3, out var v3)) IsSilver = v3.ObjectToBool();
        if (p.TryGetValue(4, out var v4)) ItemIndex = v4.ObjectToInt();
        if (p.TryGetValue(5, out var v5)) Quantity = v5.ObjectToInt();
    }
}
