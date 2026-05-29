using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Requests;

// REQUEST [321] FishingStart
public sealed class FishingStartRequest
{
    public long EventId { get; }
    public int ItemIndex { get; }
    public FishingStartRequest(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) EventId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(2, out var v2)) ItemIndex = v2.ObjectToInt();
    }
}

// REQUEST [327] FishingFinish
public sealed class FishingFinishRequest
{
    public bool Succeeded { get; }
    public FishingFinishRequest(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) Succeeded = v1.ObjectToBool();
    }
}

// REQUEST [328] FishingCancel
public sealed class FishingCancelRequest
{
    public FishingCancelRequest(Dictionary<byte, object> _) { }
}

// RESPONSE [327] FishingFinish
public sealed class FishingFinishResponse
{
    public FishingFinishResponse(Dictionary<byte, object> _) { }
}
