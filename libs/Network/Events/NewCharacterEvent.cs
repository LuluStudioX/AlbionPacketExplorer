using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [29] NewCharacter
public sealed class NewCharacterEvent
{
    public long ObjectId { get; }
    public string Name { get; }
    public Guid Guid { get; }
    public string GuildName { get; }

    public NewCharacterEvent(Dictionary<byte, object> p)
    {
        Name = GuildName = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) Name = v1?.ToString() ?? string.Empty;
        if (p.TryGetValue(7, out var v7)) Guid = v7.ObjectToGuid() ?? Guid.Empty;
        if (p.TryGetValue(8, out var v8)) GuildName = v8?.ToString() ?? string.Empty;
    }
}
