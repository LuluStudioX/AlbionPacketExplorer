using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [54] HarvestFinished
// 0=userObjectId 3=objectId 4=itemId 5=standardAmount 6=collectorBonusAmount
// 7=premiumBonusAmount 8=currentPossibleDegradationProcesses 252=54
public sealed class HarvestFinishedEvent
{
    public long UserObjectId { get; }
    public long ObjectId { get; }
    public int ItemId { get; }
    public int StandardAmount { get; }
    public int CollectorBonusAmount { get; }
    public int PremiumBonusAmount { get; }
    public int DegradationProcesses { get; }

    public HarvestFinishedEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) UserObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(3, out var v3)) ObjectId = v3.ObjectToLong() ?? 0;
        if (p.TryGetValue(4, out var v4)) ItemId = v4.ObjectToInt();
        if (p.TryGetValue(5, out var v5)) StandardAmount = v5.ObjectToInt();
        if (p.TryGetValue(6, out var v6)) CollectorBonusAmount = v6.ObjectToInt();
        if (p.TryGetValue(7, out var v7)) PremiumBonusAmount = v7.ObjectToInt();
        if (p.TryGetValue(8, out var v8)) DegradationProcesses = v8.ObjectToInt();
    }
}
