using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Numerics;

namespace AutoFete;

// Keep native calls on the framework thread. The controller calls these only from Update.
internal sealed unsafe class GameActions(ITargetManager targets, IObjectTable objects, ICondition condition)
{
    private DateTime nextJoinUtc;
    private DateTime nextInteractUtc;
    private DateTime nextActionUtc;

    // Carrying a finished stuffed toy is represented by a client condition.
    // Status 404 also indicates that the player is carrying a toy.
    public bool IsCarryingToy => condition[ConditionFlag.CarryingObject] ||
                                 condition[ConditionFlag.CarryingItem] ||
                                 objects.LocalPlayer?.StatusList.Any(s => s.StatusId == 404) == true;

    public bool CarryingObject => condition[ConditionFlag.CarryingObject];
    public bool CarryingItem => condition[ConditionFlag.CarryingItem];
    public string? LastJoinAttempt { get; private set; }
    public string? LastJoinError { get; private set; }

    public void ResetJoinStatus()
    {
        LastJoinAttempt = null;
        LastJoinError = null;
    }

    public void SelectTarget(IGameObject target)
    {
        if (targets.Target?.GameObjectId != target.GameObjectId)
            targets.Target = target;
    }

    public void ClearTarget()
    {
        if (targets.Target != null)
            targets.Target = null;
    }

    public bool IsJoined(ushort fateId)
    {
        var manager = FateManager.Instance();
        if (manager == null)
            return false;
        var fate = manager->GetFateById(fateId);
        // CurrentFate and FateJoined are set as soon as the player enters the
        // FATE area. The participant status appears after the Join FATE action.
        if (fate == null || manager->GetCurrentFateId() != fateId)
            return false;

        var participantStatus = fate->GivenStatus;
        return objects.LocalPlayer?.StatusList.Any(s =>
            (participantStatus != 0 && s.StatusId == participantStatus) ||
            s.StatusId is 2269 or 2577) == true;
    }

    public bool TryJoin(ushort fateId, Vector3 playerPosition, Vector3 fatePosition, float radius)
    {
        if (FeteCatalog.Find(fateId) == null || IsJoined(fateId) || DateTime.UtcNow < nextJoinUtc)
            return false;

        if (Vector3.Distance(playerPosition, fatePosition) > radius)
        {
            RecordJoinFailure("outside_fate_area");
            return false;
        }

        var manager = FateManager.Instance();
        if (manager == null || manager->GetFateById(fateId) == null)
        {
            RecordJoinFailure("fate_unavailable");
            return false;
        }

        var currentId = manager->GetCurrentFateId();
        if (currentId != 0 && currentId != fateId)
        {
            RecordJoinFailure($"different_fate_selected:{currentId}");
            return false;
        }

        nextJoinUtc = DateTime.UtcNow.AddSeconds(2);
        // Join FATE is the FATE level-sync request (game command 813), with
        // param1 = FATE ID and param2 = 1 to join. Sending an explicit 'on'
        // request also avoids toggling off an already-joined FATE.
        var sent = GameMain.ExecuteCommand(813, fateId, 1);
        if (sent)
        {
            LastJoinAttempt = "request_sent";
            LastJoinError = null;
        }
        else
            RecordJoinFailure("request_rejected");
        return sent;
    }

    private void RecordJoinFailure(string reason)
    {
        LastJoinAttempt = reason;
        LastJoinError = StatusText.Get("Error_JoinFailed");
    }

    public bool TryInteract(IGameObject target, Vector3 playerPosition)
    {
        if (DateTime.UtcNow < nextInteractUtc || !target.IsTargetable || target.Address == 0 ||
            Vector3.Distance(playerPosition, target.Position) > 3.5f ||
            condition[ConditionFlag.OccupiedInEvent] ||
            condition[ConditionFlag.OccupiedInQuestEvent] ||
            condition[ConditionFlag.Mounted])
            return false;

        var targetSystem = TargetSystem.Instance();
        if (targetSystem == null)
            return false;

        nextInteractUtc = DateTime.UtcNow.AddMilliseconds(750);
        targets.Target = null;
        targets.Target = target;
        long result = (long)targetSystem->InteractWithObject((GameObject*)target.Address,
            checkLineOfSight: false);
        return result > 0 && result != 7;
    }

    public IGameObject? FindTarget(FeteDefinition definition, string role) => objects
        .Where(x => x.IsTargetable && FeteTargets.Matches(definition, role, x))
        .OrderBy(x => Vector3.Distance(objects.LocalPlayer?.Position ?? Vector3.Zero, x.Position))
        .FirstOrDefault();

    public static int GetKeyItemCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory == null || inventory->GetInventoryContainer(InventoryType.KeyItems) == null
            ? -1
            : inventory->GetItemCountInContainer(itemId, InventoryType.KeyItems);
    }

    public static int GetInventoryItemCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? -1 : inventory->GetInventoryItemCount(itemId);
    }

    public static int GetFreeInventorySlots()
    {
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return 0;

        var free = 0;
        for (var type = InventoryType.Inventory1; type <= InventoryType.Inventory4; type++)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null)
                continue;
            for (var slot = 0; slot < container->Size; slot++)
            {
                var item = container->GetInventorySlot(slot);
                if (item == null || item->ItemId == 0)
                    free++;
            }
        }
        return free;
    }

    public bool CanUseInventoryItem =>
        !condition[ConditionFlag.Mounted] &&
        !condition[ConditionFlag.InCombat] &&
        !condition[ConditionFlag.Casting] &&
        !condition[ConditionFlag.Occupied] &&
        !condition[ConditionFlag.Occupied30] &&
        !condition[ConditionFlag.Occupied33] &&
        !condition[ConditionFlag.Occupied38] &&
        !condition[ConditionFlag.Occupied39] &&
        !condition[ConditionFlag.OccupiedInEvent] &&
        !condition[ConditionFlag.OccupiedInQuestEvent] &&
        !condition[ConditionFlag.OccupiedInCutSceneEvent] &&
        !condition[ConditionFlag.BetweenAreas] &&
        !condition[ConditionFlag.BetweenAreas51];

    public static bool TryUseInventoryItem(uint itemId)
    {
        var agent = AgentInventoryContext.Instance();
        return agent != null && agent->UseItem(itemId) == 0;
    }

    public bool TryUseFeteAction(ushort fateId, FeteAction action, IGameObject target, out string result)
    {
        result = StatusText.Get("Error_ActionUnavailable");
        var definition = FeteCatalog.Find(fateId);
        if (definition?.Stage != action.Stage || !IsJoined(fateId))
            return false;

        if (DateTime.UtcNow < nextActionUtc)
        {
            result = string.Empty;
            return false;
        }

        if (condition[ConditionFlag.OccupiedInEvent] ||
            condition[ConditionFlag.OccupiedInQuestEvent] ||
            condition[ConditionFlag.Mounted])
        {
            result = string.Empty;
            return false;
        }

        if (target is not ICharacter || !target.IsTargetable || target.Address == 0 ||
            !FeteTargets.Matches(definition.Value, action.TargetRole, target))
        {
            result = StatusText.Format("Error_CannotFindTarget", target.Name);
            return false;
        }

        var player = objects.LocalPlayer;
        if (player == null || Vector3.Distance(player.Position, target.Position) > 30f)
        {
            result = StatusText.Format("Error_TooFarFromTarget", target.Name);
            return false;
        }

        var manager = ActionManager.Instance();
        if (manager == null)
            return false;

        var adjusted = manager->GetAdjustedActionId(action.Id);
        if (!ActionManager.CanUseActionOnTarget(adjusted, (GameObject*)target.Address) ||
            manager->GetActionStatus(ActionType.Action, adjusted, target.GameObjectId) != 0)
        {
            result = string.Empty;
            return false;
        }

        targets.Target = target;
        nextActionUtc = DateTime.UtcNow.AddMilliseconds(650);
        var sent = manager->UseAction(ActionType.Action, adjusted, target.GameObjectId);
        result = sent ? string.Empty : StatusText.Format("Error_ActionRejected", action.Name, target.Name);
        return sent;
    }
}
