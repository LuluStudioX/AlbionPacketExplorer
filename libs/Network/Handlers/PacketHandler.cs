namespace AlbionPacketExplorer.Network.Handlers;

public abstract class PacketHandler<TPacket> : IPacketHandler
{
    private IPacketHandler? _next;

    public void SetNext(IPacketHandler handler) => _next = handler;

    public Task HandleAsync(object request)
    {
        if (request is TPacket packet)
            return OnHandleAsync(packet);
        return _next != null ? _next.HandleAsync(request) : Task.CompletedTask;
    }

    protected abstract Task OnHandleAsync(TPacket packet);

    protected Task NextAsync(object request) =>
        _next?.HandleAsync(request) ?? Task.CompletedTask;
}
