using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [176] InvitationPlayerTrade
public sealed class InvitationPlayerTradeEvent
{
    public long PartnerObjectId { get; }
    public string PartnerName { get; }
    public string PartnerGuildName { get; }
    public long TradeId { get; }

    public InvitationPlayerTradeEvent(Dictionary<byte, object> p)
    {
        PartnerName = PartnerGuildName = string.Empty;
        if (p.TryGetValue(0, out var v0)) PartnerObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) PartnerName = v1?.ToString() ?? string.Empty;
        if (p.TryGetValue(2, out var v2)) PartnerGuildName = v2?.ToString() ?? string.Empty;
        if (p.TryGetValue(6, out var v6)) TradeId = v6.ObjectToLong() ?? 0;
    }
}
