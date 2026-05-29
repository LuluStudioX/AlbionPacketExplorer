using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [165] Died
public sealed class DiedEvent
{
    public string DiedName { get; }
    public string DiedGuild { get; }
    public string KilledBy { get; }
    public string KilledByGuild { get; }

    public DiedEvent(Dictionary<byte, object> p)
    {
        DiedName = DiedGuild = KilledBy = KilledByGuild = string.Empty;
        if (p.TryGetValue(2, out var v2)) DiedName = v2?.ToString() ?? string.Empty;
        if (p.TryGetValue(3, out var v3)) DiedGuild = v3?.ToString() ?? string.Empty;
        if (p.TryGetValue(10, out var v10)) KilledBy = v10?.ToString() ?? string.Empty;
        if (p.TryGetValue(11, out var v11)) KilledByGuild = v11?.ToString() ?? string.Empty;
    }
}
