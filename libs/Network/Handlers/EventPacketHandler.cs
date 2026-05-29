namespace AlbionPacketExplorer.Network.Handlers;

public abstract class EventPacketHandler<TEvent> : PacketHandler<EventPacket>
{
    private readonly int _eventCode;

    protected EventPacketHandler(int eventCode) => _eventCode = eventCode;

    protected abstract Task OnActionAsync(TEvent value);

    protected override Task OnHandleAsync(EventPacket packet)
    {
        if (_eventCode != packet.EventCode)
            return NextAsync(packet);

        var instance = (TEvent)Activator.CreateInstance(typeof(TEvent), packet.Parameters)!;
        return OnActionAsync(instance);
    }
}

public abstract class RequestPacketHandler<TOperation> : PacketHandler<RequestPacket>
{
    private readonly int _operationCode;

    protected RequestPacketHandler(int operationCode) => _operationCode = operationCode;

    protected abstract Task OnActionAsync(TOperation value);

    protected override Task OnHandleAsync(RequestPacket packet)
    {
        if (_operationCode != packet.OperationCode)
            return NextAsync(packet);

        var instance = (TOperation)Activator.CreateInstance(typeof(TOperation), packet.Parameters)!;
        return OnActionAsync(instance);
    }
}

public abstract class ResponsePacketHandler<TOperation> : PacketHandler<ResponsePacket>
{
    private readonly int _operationCode;

    protected ResponsePacketHandler(int operationCode) => _operationCode = operationCode;

    protected abstract Task OnActionAsync(TOperation value);

    protected override Task OnHandleAsync(ResponsePacket packet)
    {
        if (_operationCode != packet.OperationCode)
            return NextAsync(packet);

        var instance = (TOperation)Activator.CreateInstance(typeof(TOperation), packet.Parameters)!;
        return OnActionAsync(instance);
    }
}
