using AlbionPacketExplorer.Network.Events;
using AlbionPacketExplorer.Network.Requests;

namespace AlbionPacketExplorer.Network.Handlers;

public sealed class NewSimpleItemEventHandler(Action<NewSimpleItemEvent> callback)
    : EventPacketHandler<NewSimpleItemEvent>((int)EventCodes.NewSimpleItem)
{
    protected override Task OnActionAsync(NewSimpleItemEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class NewLaborerItemEventHandler(Action<NewLaborerItemEvent> callback)
    : EventPacketHandler<NewLaborerItemEvent>((int)EventCodes.NewLaborerItem)
{
    protected override Task OnActionAsync(NewLaborerItemEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class LaborerObjectInfoEventHandler(Action<LaborerObjectInfoEvent> callback)
    : EventPacketHandler<LaborerObjectInfoEvent>((int)EventCodes.LaborerObjectInfo)
{
    protected override Task OnActionAsync(LaborerObjectInfoEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class LaborerObjectJobInfoEventHandler(Action<LaborerObjectJobInfoEvent> callback)
    : EventPacketHandler<LaborerObjectJobInfoEvent>((int)EventCodes.LaborerObjectJobInfo)
{
    protected override Task OnActionAsync(LaborerObjectJobInfoEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class NewBuildingEventHandler(Action<NewBuildingEvent> callback)
    : EventPacketHandler<NewBuildingEvent>((int)EventCodes.NewBuilding)
{
    protected override Task OnActionAsync(NewBuildingEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class FarmableObjectInfoEventHandler(Action<FarmableObjectInfoEvent> callback)
    : EventPacketHandler<FarmableObjectInfoEvent>((int)EventCodes.FarmableObjectInfo)
{
    protected override Task OnActionAsync(FarmableObjectInfoEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class HarvestFinishedEventHandler(Action<HarvestFinishedEvent> callback)
    : EventPacketHandler<HarvestFinishedEvent>((int)EventCodes.HarvestFinished)
{
    protected override Task OnActionAsync(HarvestFinishedEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class UpdateMoneyEventHandler(Action<UpdateMoneyEvent> callback)
    : EventPacketHandler<UpdateMoneyEvent>((int)EventCodes.UpdateMoney)
{
    protected override Task OnActionAsync(UpdateMoneyEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class UpdateFameEventHandler(Action<UpdateFameEvent> callback)
    : EventPacketHandler<UpdateFameEvent>((int)EventCodes.UpdateFame)
{
    protected override Task OnActionAsync(UpdateFameEvent value) { callback(value); return Task.CompletedTask; }
}

public sealed class ActionOnBuildingStartRequestHandler(Action<ActionOnBuildingStartRequest> callback)
    : RequestPacketHandler<ActionOnBuildingStartRequest>((int)OperationCodes.ActionOnBuildingStart)
{
    protected override Task OnActionAsync(ActionOnBuildingStartRequest value) { callback(value); return Task.CompletedTask; }
}

public sealed class ActionOnBuildingCancelRequestHandler(Action<ActionOnBuildingEndRequest> callback)
    : RequestPacketHandler<ActionOnBuildingEndRequest>((int)OperationCodes.ActionOnBuildingCancel)
{
    protected override Task OnActionAsync(ActionOnBuildingEndRequest value) { callback(value); return Task.CompletedTask; }
}
