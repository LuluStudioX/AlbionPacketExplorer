using AlbionPacketExplorer.Network.Events;
using AlbionPacketExplorer.Network.Requests;

namespace AlbionPacketExplorer.Network.Handlers;

public sealed class AlbionHandlerRegistry
{
    public event Action<NewSimpleItemEvent>?          NewSimpleItem;
    public event Action<NewLaborerItemEvent>?         NewLaborerItem;
    public event Action<LaborerObjectInfoEvent>?      LaborerObjectInfo;
    public event Action<LaborerObjectJobInfoEvent>?   LaborerObjectJobInfo;
    public event Action<NewBuildingEvent>?            NewBuilding;
    public event Action<FarmableObjectInfoEvent>?     FarmableObjectInfo;
    public event Action<HarvestFinishedEvent>?        HarvestFinished;
    public event Action<UpdateMoneyEvent>?            UpdateMoney;
    public event Action<UpdateFameEvent>?             UpdateFame;
    public event Action<ActionOnBuildingStartRequest>? ActionOnBuildingStart;
    public event Action<ActionOnBuildingEndRequest>?  ActionOnBuildingCancel;

    public AlbionNetworkParser BuildParser()
    {
        var parser = new AlbionNetworkParser();

        parser.AddEventHandler(new NewSimpleItemEventHandler(e       => NewSimpleItem?.Invoke(e)));
        parser.AddEventHandler(new NewLaborerItemEventHandler(e      => NewLaborerItem?.Invoke(e)));
        parser.AddEventHandler(new LaborerObjectInfoEventHandler(e   => LaborerObjectInfo?.Invoke(e)));
        parser.AddEventHandler(new LaborerObjectJobInfoEventHandler(e => LaborerObjectJobInfo?.Invoke(e)));
        parser.AddEventHandler(new NewBuildingEventHandler(e         => NewBuilding?.Invoke(e)));
        parser.AddEventHandler(new FarmableObjectInfoEventHandler(e  => FarmableObjectInfo?.Invoke(e)));
        parser.AddEventHandler(new HarvestFinishedEventHandler(e     => HarvestFinished?.Invoke(e)));
        parser.AddEventHandler(new UpdateMoneyEventHandler(e         => UpdateMoney?.Invoke(e)));
        parser.AddEventHandler(new UpdateFameEventHandler(e          => UpdateFame?.Invoke(e)));
        parser.AddRequestHandler(new ActionOnBuildingStartRequestHandler(r  => ActionOnBuildingStart?.Invoke(r)));
        parser.AddRequestHandler(new ActionOnBuildingCancelRequestHandler(r => ActionOnBuildingCancel?.Invoke(r)));

        return parser;
    }
}
