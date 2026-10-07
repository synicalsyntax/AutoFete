using Dalamud.Plugin.Services;
using System.Numerics;

namespace AutoFete;

// Runs only after the current Fête is joined. The first stage reacts to
// mascot pose transitions; the yak stage uses inventory state and objects.
internal sealed class FeteActionAutomation(GameActions actions, Navmesh navmesh,
    IObjectTable objects, Configuration config)
{
    private readonly Dictionary<string, ushort> lastEmoteByRole = [];
    private string? pendingPoseRole;
    private DateTime pendingPoseUntilUtc;
    private DateTime nextClipperUtc;
    private DateTime nextFleeceInteractUtc;
    private DateTime nextHandinInteractUtc;
    private int completedYakHandins;
    private int previousFleeceCount = -1;

    public void Reset()
    {
        lastEmoteByRole.Clear();
        pendingPoseRole = null;
        pendingPoseUntilUtc = DateTime.MinValue;
        nextClipperUtc = DateTime.MinValue;
        nextFleeceInteractUtc = DateTime.MinValue;
        nextHandinInteractUtc = DateTime.MinValue;
        completedYakHandins = 0;
        previousFleeceCount = -1;
    }

    public string Tick(FeteStage stage, ushort fateId) => stage switch
    {
        FeteStage.Twist => TickTwist(fateId),
        FeteStage.ShearYak => TickShearYak(fateId),
        _ => $"Joined {FeteCatalog.Find(fateId)?.Name}. Stage handler still pending.",
    };

    private string TickTwist(ushort fateId)
    {
        foreach (var action in FeteActions.All.Where(x => x.Stage == FeteStage.Twist))
        {
            var npc = actions.FindTarget(config, FeteStage.Twist, action.TargetRole);
            if (npc == null)
                continue;

            var emote = GameActions.ReadEmoteId(npc) ?? 0;
            if (!lastEmoteByRole.TryGetValue(action.TargetRole, out var previous) || emote != previous)
            {
                lastEmoteByRole[action.TargetRole] = emote;
                if (emote != 0)
                {
                    pendingPoseRole = action.TargetRole;
                    pendingPoseUntilUtc = DateTime.UtcNow.AddSeconds(10);
                }
            }
        }

        if (pendingPoseRole == null)
            return "Watching the three mascots for their next pose.";

        if (DateTime.UtcNow > pendingPoseUntilUtc)
        {
            pendingPoseRole = null;
            return "Missed a mascot pose; waiting for the next one.";
        }

        var poseRole = pendingPoseRole!;
        var poseAction = FeteActions.ForRole(FeteStage.Twist, poseRole);
        var target = actions.FindTarget(config, FeteStage.Twist, poseRole);
        if (poseAction == null || target == null)
            return $"Waiting for {poseRole} to be targetable.";

        var player = objects.LocalPlayer;
        if (player != null && Vector3.Distance(player.Position, target.Position) > 20f)
        {
            if (!navmesh.IsMoving()) navmesh.MoveNear(target.Position, 15f);
            return $"Moving into action range of {target.Name}.";
        }

        if (navmesh.IsMoving()) navmesh.Stop();
        if (actions.TryUseFeteAction(config, fateId, poseAction.Value, out var result))
            pendingPoseRole = null;
        return result;
    }

    private string TickShearYak(ushort fateId)
    {
        var fleeceCount = GameActions.GetKeyItemCount(FeteCatalog.YakFleeceEventItemId);
        if (fleeceCount < 0)
            return "Waiting for the key-item inventory to load.";

        if (previousFleeceCount >= 3 && fleeceCount == 0)
            completedYakHandins++;
        previousFleeceCount = fleeceCount;

        if (fleeceCount >= 3)
        {
            var chocobo = actions.FindTarget(config, FeteStage.ShearYak, "chirpy-chocobo");
            if (chocobo == null)
                return "Carrying three fleece; waiting for Chirpy Chocobo.";
            if (MoveWithin(chocobo.Position, 3f))
                return "Moving to Chirpy Chocobo with fleece.";
            if (DateTime.UtcNow >= nextHandinInteractUtc &&
                actions.TryInteract(chocobo, objects.LocalPlayer!.Position))
                nextHandinInteractUtc = DateTime.UtcNow.AddSeconds(2);
            return $"Turning in fleece to Chirpy Chocobo ({completedYakHandins} observed hand-ins).";
        }

        var fleece = actions.FindTarget(config, FeteStage.ShearYak, "yak-fleece");
        if (fleece != null)
        {
            if (MoveWithin(fleece.Position, 3f))
                return $"Moving to yak fleece ({fleeceCount}/3 held).";
            if (DateTime.UtcNow >= nextFleeceInteractUtc &&
                actions.TryInteract(fleece, objects.LocalPlayer!.Position))
                nextFleeceInteractUtc = DateTime.UtcNow.AddSeconds(2);
            return $"Collecting yak fleece ({fleeceCount}/3 held).";
        }

        if (DateTime.UtcNow < nextClipperUtc)
            return $"Waiting for fleece to appear ({fleeceCount}/3 held).";

        var yak = actions.FindTarget(config, FeteStage.ShearYak, "wooly-yak");
        if (yak == null)
            return "Waiting for the Wooly Yak.";
        if (MoveWithin(yak.Position, 20f))
            return "Moving into clipper range of the Wooly Yak.";

        var clipper = FeteActions.ForRole(FeteStage.ShearYak, "wooly-yak");
        if (clipper != null && actions.TryUseFeteAction(config, fateId, clipper.Value, out var actionResult))
        {
            nextClipperUtc = DateTime.UtcNow.AddSeconds(8);
            return actionResult;
        }
        return "Waiting to use the Aetheromatic Clipper on the Wooly Yak.";
    }

    private bool MoveWithin(Vector3 position, float range)
    {
        var player = objects.LocalPlayer;
        if (player == null || Vector3.Distance(player.Position, position) <= range)
        {
            if (navmesh.IsMoving()) navmesh.Stop();
            return false;
        }

        if (!navmesh.IsMoving()) navmesh.MoveNear(position, Math.Max(1f, range - 1f));
        return true;
    }
}
