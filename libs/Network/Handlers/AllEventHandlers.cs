using AlbionPacketExplorer.Network.Events;
using AlbionPacketExplorer.Network.Requests;

namespace AlbionPacketExplorer.Network.Handlers;

// ── Events ──────────────────────────────────────────────────────────────────

public sealed class LeaveEventHandler(Action<LeaveEvent> cb)
    : EventPacketHandler<LeaveEvent>((int)EventCodes.Leave)
{ protected override Task OnActionAsync(LeaveEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class HealthUpdateEventHandler(Action<HealthUpdateEvent> cb)
    : EventPacketHandler<HealthUpdateEvent>((int)EventCodes.HealthUpdate)
{ protected override Task OnActionAsync(HealthUpdateEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewCharacterEventHandler(Action<NewCharacterEvent> cb)
    : EventPacketHandler<NewCharacterEvent>((int)EventCodes.NewCharacter)
{ protected override Task OnActionAsync(NewCharacterEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewEquipmentItemEventHandler(Action<NewEquipmentItemEvent> cb)
    : EventPacketHandler<NewEquipmentItemEvent>((int)EventCodes.NewEquipmentItem)
{ protected override Task OnActionAsync(NewEquipmentItemEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewFurnitureItemEventHandler(Action<NewFurnitureItemEvent> cb)
    : EventPacketHandler<NewFurnitureItemEvent>((int)EventCodes.NewFurnitureItem)
{ protected override Task OnActionAsync(NewFurnitureItemEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewKillTrophyItemEventHandler(Action<NewKillTrophyItemEvent> cb)
    : EventPacketHandler<NewKillTrophyItemEvent>((int)EventCodes.NewKillTrophyItem)
{ protected override Task OnActionAsync(NewKillTrophyItemEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewJournalItemEventHandler(Action<NewJournalItemEvent> cb)
    : EventPacketHandler<NewJournalItemEvent>((int)EventCodes.NewJournalItem)
{ protected override Task OnActionAsync(NewJournalItemEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class FarmBuildingInfoEventHandler(Action<FarmBuildingInfoEvent> cb)
    : EventPacketHandler<FarmBuildingInfoEvent>((int)EventCodes.FarmBuildingInfo)
{ protected override Task OnActionAsync(FarmBuildingInfoEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class TakeSilverEventHandler(Action<TakeSilverEvent> cb)
    : EventPacketHandler<TakeSilverEvent>((int)EventCodes.TakeSilver)
{ protected override Task OnActionAsync(TakeSilverEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class ActionOnBuildingFinishedEventHandler(Action<ActionOnBuildingFinishedEvent> cb)
    : EventPacketHandler<ActionOnBuildingFinishedEvent>((int)EventCodes.ActionOnBuildingFinished)
{ protected override Task OnActionAsync(ActionOnBuildingFinishedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class UpdateReSpecPointsEventHandler(Action<UpdateReSpecPointsEvent> cb)
    : EventPacketHandler<UpdateReSpecPointsEvent>((int)EventCodes.UpdateReSpecPoints)
{ protected override Task OnActionAsync(UpdateReSpecPointsEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class UpdateCurrencyEventHandler(Action<UpdateCurrencyEvent> cb)
    : EventPacketHandler<UpdateCurrencyEvent>((int)EventCodes.UpdateCurrency)
{ protected override Task OnActionAsync(UpdateCurrencyEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class UpdateFactionStandingEventHandler(Action<UpdateFactionStandingEvent> cb)
    : EventPacketHandler<UpdateFactionStandingEvent>((int)EventCodes.UpdateFactionStanding)
{ protected override Task OnActionAsync(UpdateFactionStandingEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class UpdateStandingEventHandler(Action<UpdateStandingEvent> cb)
    : EventPacketHandler<UpdateStandingEvent>((int)EventCodes.UpdateStanding)
{ protected override Task OnActionAsync(UpdateStandingEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class CharacterEquipmentChangedEventHandler(Action<CharacterEquipmentChangedEvent> cb)
    : EventPacketHandler<CharacterEquipmentChangedEvent>((int)EventCodes.CharacterEquipmentChanged)
{ protected override Task OnActionAsync(CharacterEquipmentChangedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewLootEventHandler(Action<NewLootEvent> cb)
    : EventPacketHandler<NewLootEvent>((int)EventCodes.NewLoot)
{ protected override Task OnActionAsync(NewLootEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class AttachItemContainerEventHandler(Action<AttachItemContainerEvent> cb)
    : EventPacketHandler<AttachItemContainerEvent>((int)EventCodes.AttachItemContainer)
{ protected override Task OnActionAsync(AttachItemContainerEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewMobEventHandler(Action<NewMobEvent> cb)
    : EventPacketHandler<NewMobEvent>((int)EventCodes.NewMob)
{ protected override Task OnActionAsync(NewMobEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class DiedEventHandler(Action<DiedEvent> cb)
    : EventPacketHandler<DiedEvent>((int)EventCodes.Died)
{ protected override Task OnActionAsync(DiedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class InvitationPlayerTradeEventHandler(Action<InvitationPlayerTradeEvent> cb)
    : EventPacketHandler<InvitationPlayerTradeEvent>((int)EventCodes.InvitationPlayerTrade)
{ protected override Task OnActionAsync(InvitationPlayerTradeEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PlayerTradeCancelEventHandler(Action<PlayerTradeCancelEvent> cb)
    : EventPacketHandler<PlayerTradeCancelEvent>((int)EventCodes.PlayerTradeCancel)
{ protected override Task OnActionAsync(PlayerTradeCancelEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PlayerTradeUpdateEventHandler(Action<PlayerTradeUpdateEvent> cb)
    : EventPacketHandler<PlayerTradeUpdateEvent>((int)EventCodes.PlayerTradeUpdate)
{ protected override Task OnActionAsync(PlayerTradeUpdateEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PlayerTradeFinishedEventHandler(Action<PlayerTradeFinishedEvent> cb)
    : EventPacketHandler<PlayerTradeFinishedEvent>((int)EventCodes.PlayerTradeFinished)
{ protected override Task OnActionAsync(PlayerTradeFinishedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PartyJoinedEventHandler(Action<PartyJoinedEvent> cb)
    : EventPacketHandler<PartyJoinedEvent>((int)EventCodes.PartyJoined)
{ protected override Task OnActionAsync(PartyJoinedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PartyDisbandedEventHandler(Action<PartyDisbandedEvent> cb)
    : EventPacketHandler<PartyDisbandedEvent>((int)EventCodes.PartyDisbanded)
{ protected override Task OnActionAsync(PartyDisbandedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PartyPlayerJoinedEventHandler(Action<PartyPlayerJoinedEvent> cb)
    : EventPacketHandler<PartyPlayerJoinedEvent>((int)EventCodes.PartyPlayerJoined)
{ protected override Task OnActionAsync(PartyPlayerJoinedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PartyChangedOrderEventHandler(Action<PartyChangedOrderEvent> cb)
    : EventPacketHandler<PartyChangedOrderEvent>((int)EventCodes.PartyChangedOrder)
{ protected override Task OnActionAsync(PartyChangedOrderEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PartyPlayerLeftEventHandler(Action<PartyPlayerLeftEvent> cb)
    : EventPacketHandler<PartyPlayerLeftEvent>((int)EventCodes.PartyPlayerLeft)
{ protected override Task OnActionAsync(PartyPlayerLeftEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class PartySilverGainedEventHandler(Action<PartySilverGainedEvent> cb)
    : EventPacketHandler<PartySilverGainedEvent>((int)EventCodes.PartySilverGained)
{ protected override Task OnActionAsync(PartySilverGainedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class RewardGrantedEventHandler(Action<RewardGrantedEvent> cb)
    : EventPacketHandler<RewardGrantedEvent>((int)EventCodes.RewardGranted)
{ protected override Task OnActionAsync(RewardGrantedEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class InCombatStateUpdateEventHandler(Action<InCombatStateUpdateEvent> cb)
    : EventPacketHandler<InCombatStateUpdateEvent>((int)EventCodes.InCombatStateUpdate)
{ protected override Task OnActionAsync(InCombatStateUpdateEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class GrabbedLootEventHandler(Action<GrabbedLootEvent> cb)
    : EventPacketHandler<GrabbedLootEvent>((int)EventCodes.OtherGrabbedLoot)
{ protected override Task OnActionAsync(GrabbedLootEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class NewLootChestEventHandler(Action<NewLootChestEvent> cb)
    : EventPacketHandler<NewLootChestEvent>((int)EventCodes.NewLootChest)
{ protected override Task OnActionAsync(NewLootChestEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class UpdateLootChestEventHandler(Action<UpdateLootChestEvent> cb)
    : EventPacketHandler<UpdateLootChestEvent>((int)EventCodes.UpdateLootChest)
{ protected override Task OnActionAsync(UpdateLootChestEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class GuildVaultInfoEventHandler(Action<GuildVaultInfoEvent> cb)
    : EventPacketHandler<GuildVaultInfoEvent>((int)EventCodes.GuildVaultInfo)
{ protected override Task OnActionAsync(GuildVaultInfoEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class BankVaultInfoEventHandler(Action<BankVaultInfoEvent> cb)
    : EventPacketHandler<BankVaultInfoEvent>((int)EventCodes.BankVaultInfo)
{ protected override Task OnActionAsync(BankVaultInfoEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class ReceivedGvgSeasonPointsEventHandler(Action<ReceivedGvgSeasonPointsEvent> cb)
    : EventPacketHandler<ReceivedGvgSeasonPointsEvent>((int)EventCodes.ReceivedGvgSeasonPoints)
{ protected override Task OnActionAsync(ReceivedGvgSeasonPointsEvent v) { cb(v); return Task.CompletedTask; } }

public sealed class MightAndFavorReceivedEventHandler(Action<MightAndFavorReceivedEvent> cb)
    : EventPacketHandler<MightAndFavorReceivedEvent>((int)EventCodes.MightAndFavorReceivedEvent)
{ protected override Task OnActionAsync(MightAndFavorReceivedEvent v) { cb(v); return Task.CompletedTask; } }

// ── Requests ────────────────────────────────────────────────────────────────

public sealed class RegisterToObjectRequestHandler(Action<RegisterToObjectRequest> cb)
    : RequestPacketHandler<RegisterToObjectRequest>((int)OperationCodes.RegisterToObject)
{ protected override Task OnActionAsync(RegisterToObjectRequest v) { cb(v); return Task.CompletedTask; } }

public sealed class UnRegisterFromObjectRequestHandler(Action<UnRegisterFromObjectRequest> cb)
    : RequestPacketHandler<UnRegisterFromObjectRequest>((int)OperationCodes.UnRegisterFromObject)
{ protected override Task OnActionAsync(UnRegisterFromObjectRequest v) { cb(v); return Task.CompletedTask; } }

public sealed class FishingStartRequestHandler(Action<FishingStartRequest> cb)
    : RequestPacketHandler<FishingStartRequest>((int)OperationCodes.FishingStart)
{ protected override Task OnActionAsync(FishingStartRequest v) { cb(v); return Task.CompletedTask; } }

public sealed class FishingFinishRequestHandler(Action<FishingFinishRequest> cb)
    : RequestPacketHandler<FishingFinishRequest>((int)OperationCodes.FishingFinish)
{ protected override Task OnActionAsync(FishingFinishRequest v) { cb(v); return Task.CompletedTask; } }

public sealed class FishingCancelRequestHandler(Action<FishingCancelRequest> cb)
    : RequestPacketHandler<FishingCancelRequest>((int)OperationCodes.FishingCancel)
{ protected override Task OnActionAsync(FishingCancelRequest v) { cb(v); return Task.CompletedTask; } }

// ── Responses ───────────────────────────────────────────────────────────────

public sealed class FarmableHarvestResponseHandler(Action<FarmableHarvestResponse> cb)
    : ResponsePacketHandler<FarmableHarvestResponse>((int)OperationCodes.FarmableHarvest)
{ protected override Task OnActionAsync(FarmableHarvestResponse v) { cb(v); return Task.CompletedTask; } }

public sealed class FishingFinishResponseHandler(Action<FishingFinishResponse> cb)
    : ResponsePacketHandler<FishingFinishResponse>((int)OperationCodes.FishingFinish)
{ protected override Task OnActionAsync(FishingFinishResponse v) { cb(v); return Task.CompletedTask; } }

public sealed class ChangeClusterResponseHandler(Action<ChangeClusterResponse> cb)
    : ResponsePacketHandler<ChangeClusterResponse>((int)OperationCodes.ChangeCluster)
{ protected override Task OnActionAsync(ChangeClusterResponse v) { cb(v); return Task.CompletedTask; } }
