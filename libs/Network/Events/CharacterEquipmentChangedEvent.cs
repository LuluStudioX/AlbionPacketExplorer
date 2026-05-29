using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [90] CharacterEquipmentChanged
public sealed class CharacterEquipmentChangedEvent
{
    public long ObjectId { get; }
    public object? Equipment { get; }

    public CharacterEquipmentChangedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) Equipment = v2;
    }
}
