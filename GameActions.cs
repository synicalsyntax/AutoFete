using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System.Numerics;

namespace AutoFete;

// Keep native calls on the framework thread. The controller calls these only from Update.
internal sealed unsafe class GameActions(ITargetManager targets, IObjectTable objects, ICondition condition)
{
    private DateTime nextJoinUtc;
    private DateTime nextInteractUtc;
    private DateTime nextActionUtc;

    public bool IsJoined(ushort fateId)
    {
        var manager = FateManager.Instance();
        if (manager == null)
            return false;
        var fate = manager->GetFateById(fateId);
        return fate != null && (manager->IsSyncedToFate(fate) ||
                                manager->SyncedFateId == fateId ||
                                (manager->CurrentFate == fate && manager->FateJoined != 0));
    }

    public bool TryJoin(ushort fateId, Vector3 playerPosition, Vector3 fatePosition, float radius)
    {
        if (FeteCatalog.Find(fateId) == null || IsJoined(fateId) || DateTime.UtcNow < nextJoinUtc)
            return false;

        if (Vector3.Distance(playerPosition, fatePosition) > radius)
            return false;

        var manager = FateManager.Instance();
        if (manager == null || manager->GetFateById(fateId) == null)
            return false;

        var currentId = manager->GetCurrentFateId();
        if (currentId != 0 && currentId != fateId)
            return false;

        nextJoinUtc = DateTime.UtcNow.AddSeconds(2);
        // The duty-list Join FATE command is the explicit level-sync command in
        // Questionable (GameCommand.FateLevelSync = 813). The final 1 means join.
        return GameMain.ExecuteCommand(813, fateId, 1);
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
        // Match Questionable: select the object, then ask the game's target
        // system to interact. Success here only means interaction was sent.
        targets.Target = null;
        targets.Target = target;
        long result = (long)targetSystem->InteractWithObject((GameObject*)target.Address,
            checkLineOfSight: false);
        return result > 0 && result != 7;
    }

    public IGameObject? FindTarget(uint baseId) => objects
        .Where(x => x.BaseId == baseId && x.IsTargetable)
        .OrderBy(x => Vector3.Distance(objects.LocalPlayer?.Position ?? Vector3.Zero, x.Position))
        .FirstOrDefault();

    public IGameObject? FindTarget(Configuration config, FeteStage stage, string role) => objects
        .Where(x => x.IsTargetable && FeteTargets.Matches(config, stage, role, x))
        .OrderBy(x => Vector3.Distance(objects.LocalPlayer?.Position ?? Vector3.Zero, x.Position))
        .FirstOrDefault();

    public static ushort? ReadEmoteId(IGameObject actor) => actor is ICharacter && actor.Address != 0
        ? ((Character*)actor.Address)->EmoteController.EmoteId
        : null;

    public static int GetKeyItemCount(uint itemId)
    {
        var inventory = InventoryManager.Instance();
        return inventory == null || inventory->GetInventoryContainer(InventoryType.KeyItems) == null
            ? -1
            : inventory->GetItemCountInContainer(itemId, InventoryType.KeyItems);
    }

    public bool TryUseFeteAction(Configuration config, ushort fateId, FeteAction action, out string result)
    {
        result = "Action unavailable.";
        if (FeteCatalog.Find(fateId)?.Stage != action.Stage || !IsJoined(fateId))
        {
            result = "Join the matching Fête first.";
            return false;
        }

        if (DateTime.UtcNow < nextActionUtc)
        {
            result = "Waiting for the action delay.";
            return false;
        }

        if (condition[ConditionFlag.OccupiedInEvent] ||
            condition[ConditionFlag.OccupiedInQuestEvent] ||
            condition[ConditionFlag.Mounted])
        {
            result = "Cannot use a Fête action during another interaction or while mounted.";
            return false;
        }

        var target = FindTarget(config, action.Stage, action.TargetRole);
        if (target is not ICharacter || !target.IsTargetable || target.Address == 0)
        {
            result = $"No targetable NPC mapped as {action.TargetRole} is nearby.";
            return false;
        }

        var player = objects.LocalPlayer;
        if (player == null || Vector3.Distance(player.Position, target.Position) > 30f)
        {
            result = $"Move closer to {target.Name}.";
            return false;
        }

        var manager = ActionManager.Instance();
        if (manager == null)
            return false;

        var adjusted = manager->GetAdjustedActionId(action.Id);
        if (!ActionManager.CanUseActionOnTarget(adjusted, (GameObject*)target.Address) ||
            manager->GetActionStatus(ActionType.Action, adjusted, target.GameObjectId) != 0)
        {
            result = $"{action.Name} is not usable on {target.Name} right now.";
            return false;
        }

        targets.Target = target;
        nextActionUtc = DateTime.UtcNow.AddMilliseconds(650);
        var sent = manager->UseAction(ActionType.Action, adjusted, target.GameObjectId);
        result = sent
            ? $"Used {action.Name} on {target.Name}. Waiting for event credit."
            : $"The game rejected {action.Name} on {target.Name}.";
        return sent;
    }

    public uint? GetObjectiveNpc(ushort fateId)
    {
        var manager = FateManager.Instance();
        var fate = manager == null ? null : manager->GetFateById(fateId);
        return fate == null || fate->ObjectiveNpc is 0 or 0xE0000000 ? null : fate->ObjectiveNpc;
    }
}
