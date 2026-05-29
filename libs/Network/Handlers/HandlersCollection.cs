namespace AlbionPacketExplorer.Network.Handlers;

internal sealed class HandlersCollection
{
    private readonly List<IPacketHandler> _handlers = [];

    private IPacketHandler? Last => _handlers.Count > 0 ? _handlers[^1] : null;

    public void Add<TPacket>(PacketHandler<TPacket> handler)
    {
        if (Last != null) handler.SetNext(Last);
        _handlers.Add(handler);
    }

    public Task HandleAsync(object request) =>
        Last?.HandleAsync(request) ?? Task.CompletedTask;
}
