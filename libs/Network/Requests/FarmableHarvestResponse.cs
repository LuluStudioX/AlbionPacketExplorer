using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// RESPONSE [78] FarmableHarvest / PastureHarvest / PastureFeedConsumed / PastureProductHarvest
public sealed class FarmableHarvestResponse
{
    public string[] Names { get; }
    public object? Quantities { get; }
    public FarmableHarvestResponse(Dictionary<byte, object> p)
    {
        Names = [];
        if (p.TryGetValue(0, out var v0))
        {
            if (v0 is string[] sarr) Names = sarr;
            else if (v0 is string s) Names = [s];
        }
        if (p.TryGetValue(1, out var v1)) Quantities = v1;
    }
}
