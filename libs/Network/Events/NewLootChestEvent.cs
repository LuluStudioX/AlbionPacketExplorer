using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [391] NewLootChest
public sealed class NewLootChestEvent
{
    public long ObjectId { get; }
    public string UniqueName { get; }
    public string UniqueNameWithLocation { get; }
    public NewLootChestEvent(Dictionary<byte, object> p)
    {
        UniqueName = UniqueNameWithLocation = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) UniqueName = v3?.ToString() ?? string.Empty;
        if (p.TryGetValue(4, out var v4)) UniqueNameWithLocation = v4?.ToString() ?? string.Empty;
    }
}

// EVENT [392] UpdateLootChest
public sealed class UpdateLootChestEvent
{
    public long ObjectId { get; }
    public UpdateLootChestEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
    }
}
