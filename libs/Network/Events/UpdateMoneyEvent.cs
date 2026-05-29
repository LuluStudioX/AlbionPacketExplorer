using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [71] UpdateMoney
// 1=currentSilver 252=71
public sealed class UpdateMoneyEvent
{
    public long CurrentSilver { get; }

    public UpdateMoneyEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(1, out var v1)) CurrentSilver = v1.ObjectToLong() ?? 0;
    }
}
