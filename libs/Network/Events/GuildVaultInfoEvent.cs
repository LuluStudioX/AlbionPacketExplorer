using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [411] GuildVaultInfo / [412] BankVaultInfo — same shape
public sealed class GuildVaultInfoEvent
{
    public long ObjectId { get; }
    public string LocationGuid { get; }
    public GuildVaultInfoEvent(Dictionary<byte, object> p)
    {
        LocationGuid = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) LocationGuid = v1?.ToString() ?? string.Empty;
    }
}

public sealed class BankVaultInfoEvent
{
    public long ObjectId { get; }
    public string LocationGuid { get; }
    public BankVaultInfoEvent(Dictionary<byte, object> p)
    {
        LocationGuid = string.Empty;
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) LocationGuid = v1?.ToString() ?? string.Empty;
    }
}
