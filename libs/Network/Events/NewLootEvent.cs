using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [98] NewLoot
public sealed class NewLootEvent
{
    public long ObjectId { get; }
    public string LootBody { get; }

    public NewLootEvent(Dictionary<byte, object> p)
    {
        LootBody = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) LootBody = v3?.ToString() ?? string.Empty;
    }
}
