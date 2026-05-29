using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// RESPONSE [46/ChangeCluster] ChangeClusterResponse
public sealed class ChangeClusterResponse
{
    public string ClusterIndex { get; }
    public string WorldMapDataType { get; }
    public string IslandName { get; }

    public ChangeClusterResponse(Dictionary<byte, object> p)
    {
        ClusterIndex = WorldMapDataType = IslandName = string.Empty;
        if (p.TryGetValue(0, out var v0)) ClusterIndex = v0?.ToString() ?? string.Empty;
        if (p.TryGetValue(1, out var v1)) WorldMapDataType = v1?.ToString() ?? string.Empty;
        if (p.TryGetValue(2, out var v2)) IslandName = v2?.ToString() ?? string.Empty;
    }
}
