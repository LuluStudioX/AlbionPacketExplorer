using AlbionPacketExplorer.Network.Events;
using AlbionPacketExplorer.Network.Requests;

namespace AlbionPacketExplorer.Network.Handlers;

public sealed class AlbionHandlerRegistry
{
    // Island / Laborer
    public event Action<NewSimpleItemEvent>?           NewSimpleItem;
    public event Action<NewLaborerItemEvent>?          NewLaborerItem;
    public event Action<LaborerObjectInfoEvent>?       LaborerObjectInfo;
    public event Action<LaborerObjectJobInfoEvent>?    LaborerObjectJobInfo;
    public event Action<NewBuildingEvent>?             NewBuilding;
    public event Action<FarmableObjectInfoEvent>?      FarmableObjectInfo;
    public event Action<FarmBuildingInfoEvent>?        FarmBuildingInfo;
    public event Action<HarvestFinishedEvent>?         HarvestFinished;
    public event Action<ActionOnBuildingFinishedEvent>? ActionOnBuildingFinished;

    // Economy / Silver / Fame
    public event Action<UpdateMoneyEvent>?             UpdateMoney;
    public event Action<UpdateFameEvent>?              UpdateFame;
    public event Action<TakeSilverEvent>?              TakeSilver;
    public event Action<UpdateReSpecPointsEvent>?      UpdateReSpecPoints;
    public event Action<UpdateCurrencyEvent>?          UpdateCurrency;
    public event Action<UpdateFactionStandingEvent>?   UpdateFactionStanding;
    public event Action<UpdateStandingEvent>?          UpdateStanding;
    public event Action<MightAndFavorReceivedEvent>?   MightAndFavorReceived;
    public event Action<ReceivedGvgSeasonPointsEvent>? ReceivedGvgSeasonPoints;

    // Items / Loot
    public event Action<NewEquipmentItemEvent>?        NewEquipmentItem;
    public event Action<NewFurnitureItemEvent>?        NewFurnitureItem;
    public event Action<NewJournalItemEvent>?          NewJournalItem;
    public event Action<NewKillTrophyItemEvent>?       NewKillTrophyItem;
    public event Action<NewLootEvent>?                 NewLoot;
    public event Action<GrabbedLootEvent>?             OtherGrabbedLoot;
    public event Action<NewLootChestEvent>?            NewLootChest;
    public event Action<UpdateLootChestEvent>?         UpdateLootChest;
    public event Action<AttachItemContainerEvent>?     AttachItemContainer;
    public event Action<RewardGrantedEvent>?           RewardGranted;

    // Characters / Combat
    public event Action<LeaveEvent>?                   Leave;
    public event Action<NewCharacterEvent>?            NewCharacter;
    public event Action<NewMobEvent>?                  NewMob;
    public event Action<DiedEvent>?                    Died;
    public event Action<HealthUpdateEvent>?            HealthUpdate;
    public event Action<InCombatStateUpdateEvent>?     InCombatStateUpdate;
    public event Action<CharacterEquipmentChangedEvent>? CharacterEquipmentChanged;

    // Party
    public event Action<PartyJoinedEvent>?             PartyJoined;
    public event Action<PartyDisbandedEvent>?          PartyDisbanded;
    public event Action<PartyPlayerJoinedEvent>?       PartyPlayerJoined;
    public event Action<PartyChangedOrderEvent>?       PartyChangedOrder;
    public event Action<PartyPlayerLeftEvent>?         PartyPlayerLeft;
    public event Action<PartySilverGainedEvent>?       PartySilverGained;

    // Trade
    public event Action<InvitationPlayerTradeEvent>?   InvitationPlayerTrade;
    public event Action<PlayerTradeCancelEvent>?       PlayerTradeCancel;
    public event Action<PlayerTradeUpdateEvent>?       PlayerTradeUpdate;
    public event Action<PlayerTradeFinishedEvent>?     PlayerTradeFinished;

    // Vault
    public event Action<GuildVaultInfoEvent>?          GuildVaultInfo;
    public event Action<BankVaultInfoEvent>?           BankVaultInfo;

    // Requests
    public event Action<ActionOnBuildingStartRequest>?  ActionOnBuildingStart;
    public event Action<ActionOnBuildingEndRequest>?    ActionOnBuildingCancel;
    public event Action<RegisterToObjectRequest>?       RegisterToObject;
    public event Action<UnRegisterFromObjectRequest>?   UnRegisterFromObject;
    public event Action<FishingStartRequest>?           FishingStart;
    public event Action<FishingFinishRequest>?          FishingFinish;
    public event Action<FishingCancelRequest>?          FishingCancel;

    // Responses
    public event Action<FarmableHarvestResponse>?       FarmableHarvest;
    public event Action<FishingFinishResponse>?         FishingFinishResponse;
    public event Action<ChangeClusterResponse>?         ChangeCluster;

    public AlbionNetworkParser BuildParser()
    {
        var p = new AlbionNetworkParser();

        // Island
        p.AddEventHandler(new NewSimpleItemEventHandler(e           => NewSimpleItem?.Invoke(e)));
        p.AddEventHandler(new NewLaborerItemEventHandler(e          => NewLaborerItem?.Invoke(e)));
        p.AddEventHandler(new LaborerObjectInfoEventHandler(e       => LaborerObjectInfo?.Invoke(e)));
        p.AddEventHandler(new LaborerObjectJobInfoEventHandler(e    => LaborerObjectJobInfo?.Invoke(e)));
        p.AddEventHandler(new NewBuildingEventHandler(e             => NewBuilding?.Invoke(e)));
        p.AddEventHandler(new FarmableObjectInfoEventHandler(e      => FarmableObjectInfo?.Invoke(e)));
        p.AddEventHandler(new FarmBuildingInfoEventHandler(e        => FarmBuildingInfo?.Invoke(e)));
        p.AddEventHandler(new HarvestFinishedEventHandler(e         => HarvestFinished?.Invoke(e)));
        p.AddEventHandler(new ActionOnBuildingFinishedEventHandler(e => ActionOnBuildingFinished?.Invoke(e)));

        // Economy
        p.AddEventHandler(new UpdateMoneyEventHandler(e             => UpdateMoney?.Invoke(e)));
        p.AddEventHandler(new UpdateFameEventHandler(e              => UpdateFame?.Invoke(e)));
        p.AddEventHandler(new TakeSilverEventHandler(e              => TakeSilver?.Invoke(e)));
        p.AddEventHandler(new UpdateReSpecPointsEventHandler(e      => UpdateReSpecPoints?.Invoke(e)));
        p.AddEventHandler(new UpdateCurrencyEventHandler(e          => UpdateCurrency?.Invoke(e)));
        p.AddEventHandler(new UpdateFactionStandingEventHandler(e   => UpdateFactionStanding?.Invoke(e)));
        p.AddEventHandler(new UpdateStandingEventHandler(e          => UpdateStanding?.Invoke(e)));
        p.AddEventHandler(new MightAndFavorReceivedEventHandler(e   => MightAndFavorReceived?.Invoke(e)));
        p.AddEventHandler(new ReceivedGvgSeasonPointsEventHandler(e => ReceivedGvgSeasonPoints?.Invoke(e)));

        // Items / Loot
        p.AddEventHandler(new NewEquipmentItemEventHandler(e        => NewEquipmentItem?.Invoke(e)));
        p.AddEventHandler(new NewFurnitureItemEventHandler(e        => NewFurnitureItem?.Invoke(e)));
        p.AddEventHandler(new NewJournalItemEventHandler(e          => NewJournalItem?.Invoke(e)));
        p.AddEventHandler(new NewKillTrophyItemEventHandler(e       => NewKillTrophyItem?.Invoke(e)));
        p.AddEventHandler(new NewLootEventHandler(e                 => NewLoot?.Invoke(e)));
        p.AddEventHandler(new GrabbedLootEventHandler(e             => OtherGrabbedLoot?.Invoke(e)));
        p.AddEventHandler(new NewLootChestEventHandler(e            => NewLootChest?.Invoke(e)));
        p.AddEventHandler(new UpdateLootChestEventHandler(e         => UpdateLootChest?.Invoke(e)));
        p.AddEventHandler(new AttachItemContainerEventHandler(e     => AttachItemContainer?.Invoke(e)));
        p.AddEventHandler(new RewardGrantedEventHandler(e           => RewardGranted?.Invoke(e)));

        // Characters / Combat
        p.AddEventHandler(new LeaveEventHandler(e                   => Leave?.Invoke(e)));
        p.AddEventHandler(new NewCharacterEventHandler(e            => NewCharacter?.Invoke(e)));
        p.AddEventHandler(new NewMobEventHandler(e                  => NewMob?.Invoke(e)));
        p.AddEventHandler(new DiedEventHandler(e                    => Died?.Invoke(e)));
        p.AddEventHandler(new HealthUpdateEventHandler(e            => HealthUpdate?.Invoke(e)));
        p.AddEventHandler(new InCombatStateUpdateEventHandler(e     => InCombatStateUpdate?.Invoke(e)));
        p.AddEventHandler(new CharacterEquipmentChangedEventHandler(e => CharacterEquipmentChanged?.Invoke(e)));

        // Party
        p.AddEventHandler(new PartyJoinedEventHandler(e             => PartyJoined?.Invoke(e)));
        p.AddEventHandler(new PartyDisbandedEventHandler(e          => PartyDisbanded?.Invoke(e)));
        p.AddEventHandler(new PartyPlayerJoinedEventHandler(e       => PartyPlayerJoined?.Invoke(e)));
        p.AddEventHandler(new PartyChangedOrderEventHandler(e       => PartyChangedOrder?.Invoke(e)));
        p.AddEventHandler(new PartyPlayerLeftEventHandler(e         => PartyPlayerLeft?.Invoke(e)));
        p.AddEventHandler(new PartySilverGainedEventHandler(e       => PartySilverGained?.Invoke(e)));

        // Trade
        p.AddEventHandler(new InvitationPlayerTradeEventHandler(e   => InvitationPlayerTrade?.Invoke(e)));
        p.AddEventHandler(new PlayerTradeCancelEventHandler(e       => PlayerTradeCancel?.Invoke(e)));
        p.AddEventHandler(new PlayerTradeUpdateEventHandler(e       => PlayerTradeUpdate?.Invoke(e)));
        p.AddEventHandler(new PlayerTradeFinishedEventHandler(e     => PlayerTradeFinished?.Invoke(e)));

        // Vault
        p.AddEventHandler(new GuildVaultInfoEventHandler(e          => GuildVaultInfo?.Invoke(e)));
        p.AddEventHandler(new BankVaultInfoEventHandler(e           => BankVaultInfo?.Invoke(e)));

        // Requests
        p.AddRequestHandler(new ActionOnBuildingStartRequestHandler(r  => ActionOnBuildingStart?.Invoke(r)));
        p.AddRequestHandler(new ActionOnBuildingCancelRequestHandler(r => ActionOnBuildingCancel?.Invoke(r)));
        p.AddRequestHandler(new RegisterToObjectRequestHandler(r       => RegisterToObject?.Invoke(r)));
        p.AddRequestHandler(new UnRegisterFromObjectRequestHandler(r   => UnRegisterFromObject?.Invoke(r)));
        p.AddRequestHandler(new FishingStartRequestHandler(r           => FishingStart?.Invoke(r)));
        p.AddRequestHandler(new FishingFinishRequestHandler(r          => FishingFinish?.Invoke(r)));
        p.AddRequestHandler(new FishingCancelRequestHandler(r          => FishingCancel?.Invoke(r)));

        // Responses
        p.AddResponseHandler(new FarmableHarvestResponseHandler(r      => FarmableHarvest?.Invoke(r)));
        p.AddResponseHandler(new FishingFinishResponseHandler(r        => FishingFinishResponse?.Invoke(r)));
        p.AddResponseHandler(new ChangeClusterResponseHandler(r        => ChangeCluster?.Invoke(r)));

        return p;
    }
}
