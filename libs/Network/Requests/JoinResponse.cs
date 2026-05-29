using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// RESPONSE [Join] JoinResponse — key params for player state
public sealed class JoinResponse
{
    public long UserObjectId { get; }
    public Guid UserGuid { get; }
    public string Username { get; }
    public string MapIndex { get; }
    public long Silver { get; }
    public long Gold { get; }
    public string GuildName { get; }
    public string AllianceName { get; }

    public JoinResponse(Dictionary<byte, object> p)
    {
        Username = MapIndex = GuildName = AllianceName = string.Empty;
        if (p.TryGetValue(0, out var v0)) UserObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) UserGuid = v1.ObjectToGuid() ?? Guid.Empty;
        if (p.TryGetValue(2, out var v2)) Username = v2?.ToString() ?? string.Empty;
        if (p.TryGetValue(8, out var v8)) MapIndex = v8?.ToString() ?? string.Empty;
        if (p.TryGetValue(33, out var v33)) Silver = v33.ObjectToLong() ?? 0;
        if (p.TryGetValue(34, out var v34)) Gold = v34.ObjectToLong() ?? 0;
        if (p.TryGetValue(58, out var v58)) GuildName = v58?.ToString() ?? string.Empty;
        if (p.TryGetValue(79, out var v79)) AllianceName = v79?.ToString() ?? string.Empty;
    }
}
