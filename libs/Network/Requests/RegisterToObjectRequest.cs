using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// REQUEST [50] RegisterToObject
public sealed class RegisterToObjectRequest
{
    public long BuildingObjectId { get; }
    public RegisterToObjectRequest(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) BuildingObjectId = v0.ObjectToLong() ?? 0;
    }
}

// REQUEST [51] UnRegisterFromObject
public sealed class UnRegisterFromObjectRequest
{
    public long BuildingObjectId { get; }
    public UnRegisterFromObjectRequest(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) BuildingObjectId = v0.ObjectToLong() ?? 0;
    }
}
