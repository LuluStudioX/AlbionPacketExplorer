using AlbionPacketExplorer.Network.Handlers;

namespace AlbionPacketExplorer.Network.Events;

// EVENT [99] AttachItemContainer
public sealed class AttachItemContainerEvent
{
    public long ObjectId { get; }
    public Guid ContainerGuid { get; }
    public Guid PrivateContainerGuid { get; }
    public object? ContainerSlots { get; }

    public AttachItemContainerEvent(Dictionary<byte, object> p)
    {
        if (p.TryGetValue(0, out var v0)) ObjectId = v0.ObjectToLong() ?? 0;
        if (p.TryGetValue(1, out var v1)) ContainerGuid = v1.ObjectToGuid() ?? Guid.Empty;
        if (p.TryGetValue(2, out var v2)) PrivateContainerGuid = v2.ObjectToGuid() ?? Guid.Empty;
        if (p.TryGetValue(3, out var v3)) ContainerSlots = v3;
    }
}
